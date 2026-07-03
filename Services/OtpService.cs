using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class OtpService : IOtpService
{
    private readonly AppDbContext _db;
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(4);
    private const int MaxFailedAttempts = 5;

    public OtpService(AppDbContext db) => _db = db;

    private static string GenerateCode() => Random.Shared.Next(0, 1_000_000).ToString("D6");

    public async Task<(int count, byte[] excelBytes)> GenerateBulkOtpsAsync()
    {
        var pendingUsers = await _db.Users
            .Where(u => u.Role == "User" && u.PasswordHash == null)
            .ToListAsync();

        var results = new List<(string Email, string Phone, string Otp)>();

        foreach (var user in pendingUsers)
        {
            // Replace any existing unused OTP for this user with a fresh one.
            await _db.PasswordResetOtps.Where(o => o.UserId == user.Id && !o.IsUsed).ExecuteDeleteAsync();

            var code = GenerateCode();
            _db.PasswordResetOtps.Add(new PasswordResetOtp
            {
                UserId = user.Id,
                OtpCode = code,
                ExpiresAt = DateTime.UtcNow.Add(Expiry),
            });
            results.Add((user.Email, user.Phone, code));
        }

        if (results.Count > 0) await _db.SaveChangesAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("OTPs");
        var headers = new[] { "Email", "Phone", "OTP" };
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
            ws.Cell(i + 2, 1).Value = results[i].Email;
            ws.Cell(i + 2, 2).Value = results[i].Phone;
            ws.Cell(i + 2, 3).Value = results[i].Otp;
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return (results.Count, stream.ToArray());
    }

    public async Task<string?> GenerateSingleOtpAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.Role != "User") return null;

        await _db.PasswordResetOtps.Where(o => o.UserId == user.Id && !o.IsUsed).ExecuteDeleteAsync();

        var code = GenerateCode();
        _db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            OtpCode = code,
            ExpiresAt = DateTime.UtcNow.Add(Expiry),
        });
        await _db.SaveChangesAsync();
        return code;
    }

    public async Task<(bool success, string message)> VerifyAndResetPasswordAsync(string identifier, string otpCode, string newPassword)
    {
        identifier = identifier.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Role == "User" && (u.Email == identifier.ToLower() || u.Phone == identifier));
        if (user == null) return (false, "No account found with that email or phone.");

        var otp = await _db.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (otp == null) return (false, "No active OTP for this account. Ask your admin for a new one.");

        if (otp.OtpCode != otpCode.Trim())
        {
            otp.FailedAttempts++;
            if (otp.FailedAttempts >= MaxFailedAttempts) otp.IsUsed = true;
            await _db.SaveChangesAsync();
            return otp.IsUsed
                ? (false, "Too many wrong attempts. Ask your admin for a new OTP.")
                : (false, $"Incorrect code. {MaxFailedAttempts - otp.FailedAttempts} attempt(s) left.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        otp.IsUsed = true;
        await _db.SaveChangesAsync();
        return (true, "Password set successfully. You can now log in.");
    }
}
