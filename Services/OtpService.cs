using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Resources;

namespace MvcApp.Services;

public class OtpService : IOtpService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _L;
    private readonly ILogger<OtpService> _logger;
    private readonly IAuthService _auth;
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(24);
    private const int MaxFailedAttempts = 5;

    public OtpService(AppDbContext db, IStringLocalizer<SharedResource> localizer, ILogger<OtpService> logger, IAuthService auth)
    {
        _db = db;
        _L = localizer;
        _logger = logger;
        _auth = auth;
    }

    private static string GenerateCode() => Random.Shared.Next(0, 1_000_000).ToString("D6");

    // Portal link and welcome wording for the ready-to-send SMS Message
    // column in the "Generate Default Passwords" export — kept in English
    // regardless of the admin's UI language, since it's sent as-is to the
    // new user via an external SMS/portal tool.
    private const string PortalUrl = "https://mcd-crew-hub.runasp.net/login";
    private const string WelcomeMessage = "Welcome to McDonald's Crew Insights Hub!";

    private static string BuildSmsMessage(string email, string password) =>
        $"{WelcomeMessage}\n{PortalUrl}\nUsername: {email}\nTemporary Password: {password}";

    public async Task<(int count, byte[] excelBytes)> GenerateBulkDefaultPasswordsAsync()
    {
        var pendingUsers = await _db.Users
            .Where(u => u.Role != "Admin" && u.PasswordHash == null)
            .ToListAsync();

        var results = new List<(string Email, string Phone, string Password)>();

        foreach (var user in pendingUsers)
        {
            var password = PasswordPolicy.GenerateTemporaryPassword();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.MustChangePassword = true;
            results.Add((user.Email, user.Phone, password));
        }

        if (results.Count > 0) await _db.SaveChangesAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Default Passwords");
        var headers = new[] { "Email", "Phone", "Temporary Password", "SMS Message" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        for (int i = 0; i < results.Count; i++)
        {
            var row = i + 2;
            ws.Cell(row, 1).Value = results[i].Email;
            ws.Cell(row, 2).Value = results[i].Phone;
            ws.Cell(row, 3).Value = results[i].Password;

            var smsCell = ws.Cell(row, 4);
            smsCell.Value = BuildSmsMessage(results[i].Email, results[i].Password);
            // Real line breaks inside the cell (not a delimiter) — copying
            // this cell into an SMS/portal tool sends each line on its own,
            // exactly as it's laid out here.
            smsCell.Style.Alignment.WrapText = true;
            smsCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 18;
        ws.Column(4).Width = 55;
        ws.Rows().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return (results.Count, stream.ToArray());
    }

    public async Task<string?> GenerateSingleOtpAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.Role == "Admin") return null;

        await _db.PasswordResetOtps.Where(o => o.UserId == user.Id && !o.IsUsed).ExecuteDeleteAsync();

        var code = GenerateCode();
        _db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            OtpCode = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.Add(Expiry),
        });
        await _db.SaveChangesAsync();
        return code;
    }

    public async Task<(bool success, string message)> VerifyAndResetPasswordAsync(string identifier, string otpCode, string newPassword)
    {
        identifier = identifier.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Role != "Admin" && (u.Email == identifier.ToLower() || u.Phone == identifier));
        // Same message as "no active OTP" below (not Msg_NoAccountFound) —
        // an unauthenticated caller must not be able to tell "no such
        // account" apart from "account exists but nothing to reset right
        // now", which would otherwise let this form be used to enumerate
        // registered emails/phones.
        if (user == null) return (false, _L["Msg_NoActiveOtp"].Value);

        var otp = await _db.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (otp == null) return (false, _L["Msg_NoActiveOtp"].Value);

        if (!BCrypt.Net.BCrypt.Verify(otpCode.Trim(), otp.OtpCode))
        {
            otp.FailedAttempts++;
            if (otp.FailedAttempts >= MaxFailedAttempts) otp.IsUsed = true;
            await _db.SaveChangesAsync();
            return otp.IsUsed
                ? (false, _L["Msg_TooManyOtpAttempts"].Value)
                : (false, string.Format(_L["Msg_IncorrectOtpCode"].Value, MaxFailedAttempts - otp.FailedAttempts));
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        otp.IsUsed = true;
        await _db.SaveChangesAsync();
        return (true, _L["Msg_PasswordSetSuccess"].Value);
    }

    // ── Admin password reset (Super Admin issues the OTP; Master Recovery
    // Key stays Super-Admin-only and is never used for other Admins) ──────

    public async Task<string?> GenerateAdminResetOtpAsync(int targetUserId, string requestingEmail)
    {
        if (!SuperAdminPolicy.IsSuperAdmin(requestingEmail)) return null;

        var user = await _db.Users.FindAsync(targetUserId);
        // Target must be an Admin, and must not be the Super Admin — the
        // Super Admin recovers their own account via the Master Key, never
        // via OTP (this OTP never logs anyone in directly either way).
        if (user == null || user.Role != "Admin" || SuperAdminPolicy.IsSuperAdmin(user.Email)) return null;

        await _db.PasswordResetOtps.Where(o => o.UserId == user.Id && !o.IsUsed).ExecuteDeleteAsync();

        var code = GenerateCode();
        _db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            OtpCode = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.Add(Expiry),
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("Super Admin '{RequestingEmail}' generated a password-reset OTP for Admin '{TargetEmail}' (user id {TargetUserId}).",
            requestingEmail.Trim().ToLowerInvariant(), user.Email, user.Id);

        return code;
    }

    public async Task<(bool success, string message)> VerifyAndResetAdminPasswordAsync(string identifier, string otpCode, string newPassword)
    {
        identifier = identifier.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Role == "Admin" && (u.Email == identifier.ToLower() || u.Phone == identifier));
        // Excludes the Super Admin (checked in-memory since EF can't translate
        // the static helper) so this path can never touch that account, and
        // reuses the same generic "no active OTP" message as the User flow to
        // avoid leaking account existence.
        if (user == null || SuperAdminPolicy.IsSuperAdmin(user.Email)) return (false, _L["Msg_NoActiveOtp"].Value);

        var otp = await _db.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (otp == null) return (false, _L["Msg_NoActiveOtp"].Value);

        if (!BCrypt.Net.BCrypt.Verify(otpCode.Trim(), otp.OtpCode))
        {
            otp.FailedAttempts++;
            if (otp.FailedAttempts >= MaxFailedAttempts) otp.IsUsed = true;
            await _db.SaveChangesAsync();
            return otp.IsUsed
                ? (false, _L["Msg_TooManyOtpAttempts"].Value)
                : (false, string.Format(_L["Msg_IncorrectOtpCode"].Value, MaxFailedAttempts - otp.FailedAttempts));
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        otp.IsUsed = true;
        await _db.SaveChangesAsync();
        _auth.ClearLockout(user.Email);

        _logger.LogInformation("Admin '{Email}' (user id {UserId}) reset their password via a Super-Admin-issued OTP.", user.Email, user.Id);

        return (true, _L["Msg_PasswordSetSuccess"].Value);
    }
}
