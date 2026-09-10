using System.Security.Cryptography;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Thrown by UploadBulkUsersAsync when a non-Super-Admin's file contains one
/// or more rows that would create an Admin account. The whole upload is
/// rejected — nothing in the file is persisted, not even the valid rows —
/// rather than silently skipping just the offending rows.
/// </summary>
public sealed class BulkUploadRoleForbiddenException : Exception
{
    public IReadOnlyList<int> Rows { get; }

    public BulkUploadRoleForbiddenException(IReadOnlyList<int> rows)
        : base("Bulk upload rejected: one or more rows attempt to create an Admin account.") =>
        Rows = rows;
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly ISessionValidationService _sessionValidation;

    public UserService(AppDbContext db, IStoreAccessService storeAccess, ISessionValidationService sessionValidation)
    {
        _db = db;
        _storeAccess = storeAccess;
        _sessionValidation = sessionValidation;
    }

    private static UserViewModel ToVm(User u) => new()
    {
        Id = u.Id, Email = u.Email, Phone = u.Phone, Role = u.Role, AssignedName = u.AssignedName ?? "",
        HasPassword = !string.IsNullOrEmpty(u.PasswordHash), MustChangePassword = u.MustChangePassword, CreatedAt = u.CreatedAt
    };

    public async Task<List<UserViewModel>> GetAllAsync(string actorEmail)
    {
        var users = (await _db.Users.OrderBy(u => u.CreatedAt).ToListAsync())
            .Where(u => UserManagementPolicy.CanView(actorEmail, u))
            .Select(ToVm).ToList();

        // Store-restricted roles only — Admin/User aren't matched against
        // StoreReference, so leave MatchedStoreCount null for them (there's
        // nothing meaningful to count).
        foreach (var u in users.Where(u => _storeAccess.IsRestrictedRole(u.Role)))
        {
            var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(u.Role, u.Email);
            u.MatchedStoreCount = accessible?.Count ?? 0;
        }

        return users;
    }

    public async Task<UserViewModel?> GetByIdAsync(int id, string actorEmail)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null || !UserManagementPolicy.CanView(actorEmail, u)) return null;
        return ToVm(u);
    }

    public async Task<UserLoginHistoryViewModel?> GetLoginHistoryAsync(int id, string actorEmail)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null || !UserManagementPolicy.CanView(actorEmail, u)) return null;

        var logins = (await _db.LoginHistories
            .Where(l => l.UserId == id)
            .OrderByDescending(l => l.LoggedInAt)
            .Take(200)
            .ToListAsync())
            .Select(l =>
            {
                var (browser, os) = UserAgentParser.Parse(l.UserAgent);
                return new LoginHistoryItem
                {
                    LoggedInAt = l.LoggedInAt, IpAddress = l.IpAddress, UserAgent = l.UserAgent,
                    Success = l.Success, Portal = l.Portal, FailureReason = l.FailureReason,
                    Browser = browser, OperatingSystem = os,
                };
            })
            .ToList();

        return new UserLoginHistoryViewModel { UserId = u.Id, Email = u.Email, AssignedName = u.AssignedName ?? "", Logins = logins };
    }

    public async Task<(UserViewModel? user, string? error, string? temporaryPassword)> CreateAsync(CreateUserViewModel vm, string actorEmail)
    {
        if (!UserManagementPolicy.IsValidRole(vm.Role)) return (null, "invalid-role", null);
        var role = UserManagementPolicy.NormalizeRole(vm.Role)!;
        if (!UserManagementPolicy.CanAssignRole(actorEmail, role)) return (null, "role-forbidden", null);

        var email = vm.Email.ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return (null, "duplicate-email", null);

        // The Admin never types a password here — a compliant temporary one
        // is generated instead, shown once so it can be sent to the new user,
        // and MustChangePassword forces them to replace it on first login.
        var temporaryPassword = PasswordPolicy.GenerateTemporaryPassword();
        var user = new User
        {
            Email = email,
            Phone = vm.Phone,
            AssignedName = vm.AssignedName?.Trim() ?? "",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
            Role = role,
            MustChangePassword = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (ToVm(user), null, temporaryPassword);
    }

    private async Task<bool> IsLastAdminAsync() => await _db.Users.CountAsync(u => u.Role == "Admin") <= 1;

    public async Task<(UserViewModel? user, string? error)> UpdateAsync(int id, EditUserViewModel vm, string actorEmail)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || !UserManagementPolicy.CanEdit(actorEmail, user)) return (null, null);

        if (!UserManagementPolicy.IsValidRole(vm.Role)) return (null, "invalid-role");
        var role = UserManagementPolicy.NormalizeRole(vm.Role)!;
        if (!UserManagementPolicy.CanChangeRole(actorEmail, user, role)) return (null, "role-forbidden");

        if (user.Role == "Admin" && role != "Admin" && await IsLastAdminAsync())
            return (null, "last-admin");

        var email = vm.Email.ToLower();
        if (!UserManagementPolicy.CanChangeEmail(user, email)) return (null, "super-admin-protected");
        if (email != user.Email && await _db.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return (null, "duplicate-email");

        user.Email = email;
        user.Phone = vm.Phone;
        user.AssignedName = vm.AssignedName?.Trim() ?? "";
        user.Role = role;
        if (!string.IsNullOrEmpty(vm.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password);
        await _db.SaveChangesAsync();
        // A role change (or the account being edited at all) must not keep
        // working off a stale cached session-validation result — see
        // SessionValidationService.
        _sessionValidation.Invalidate(id);
        return (ToVm(user), null);
    }

    public async Task<(bool success, string? error)> DeleteAsync(int id, string actorEmail)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (false, null);

        // The Super Admin account can never be deleted, by anyone — including
        // itself. A normal Admin targeting it gets the same generic
        // not-found as any other hidden Admin row (no existence leak); the
        // Super Admin targeting itself gets a specific, informative error
        // (it already knows its own id/email, so nothing is disclosed).
        if (SuperAdminPolicy.IsSuperAdmin(user.Email))
            return (false, SuperAdminPolicy.IsSuperAdmin(actorEmail) ? "super-admin-protected" : null);

        if (!UserManagementPolicy.CanDelete(actorEmail, user)) return (false, null);
        if (user.Role == "Admin" && await IsLastAdminAsync())
            return (false, "last-admin");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        _sessionValidation.Invalidate(id);
        return (true, null);
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string key)
    {
        var setting = await _db.AppSettings.FindAsync("admin_recovery_key_hash");
        if (setting == null || string.IsNullOrEmpty(setting.Value)) return false;
        return BCrypt.Net.BCrypt.Verify(key, setting.Value);
    }

    // The Master Recovery Key is an emergency mechanism for the single Super
    // Admin (admin@mcd.com) only — it must never be usable to take over any
    // other Admin account, even by someone who legitimately holds the key.
    // Other Admins get a password reset via a Super-Admin-issued OTP instead
    // (see OtpService.GenerateAdminResetOtpAsync/VerifyAndResetAdminPasswordAsync).
    public async Task<bool> ResetAdminPasswordAsync(string email, string newPassword)
    {
        if (!SuperAdminPolicy.IsSuperAdmin(email)) return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.Role == "Admin");
        if (user == null) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateRecoveryKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // Only the Super Admin may generate/regenerate the Master Recovery Key —
    // enforced here server-side (not just hidden in the UI), independent of
    // the re-authentication (current password) check below.
    public async Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password)
    {
        if (!SuperAdminPolicy.IsSuperAdmin(requestingEmail)) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == requestingEmail.ToLower() && u.Role == "Admin");
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        var key = GenerateRecoveryKey();
        var hash = BCrypt.Net.BCrypt.HashPassword(key);

        var setting = await _db.AppSettings.FindAsync("admin_recovery_key_hash");
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = "admin_recovery_key_hash", Value = hash });
        else setting.Value = hash;
        await _db.SaveChangesAsync();

        return key;
    }

    private static string Col(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return row.Cell(col.Address.ColumnNumber).GetString().Trim();
        }
        return "";
    }

    public IReadOnlyList<string> ValidRoles => UserManagementPolicy.ValidRoles;

    public async Task<(int created, IReadOnlyList<string> skippedEmails, IReadOnlyList<string> roleMismatches)> UploadBulkUsersAsync(IFormFile file, string actorEmail)
    {
        const long maxBytes = 10 * 1024 * 1024;
        if (file.Length > maxBytes) throw new InvalidOperationException("File size exceeds the 10 MB limit.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls") throw new InvalidOperationException("Only Excel files (.xlsx / .xls) are allowed.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        var existingEmails = (await _db.Users.Select(u => u.Email).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<User>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forbiddenAdminRows = new List<int>();
        var skippedEmails = new List<string>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var email = Col(row, ws, "Email", "email").ToLower();
            var phone = Col(row, ws, "Phone", "Phone Number", "phone");
            var assignedName = Col(row, ws, "Assigned Name", "AssignedName", "Name", "Display Name");
            var roleRaw = Col(row, ws, "Role", "role");
            if (string.IsNullOrWhiteSpace(email)) continue;

            // Skip accounts that already exist (by email) so re-uploading a
            // file never overwrites an activated user, and skip in-file
            // duplicates too.
            if (existingEmails.Contains(email) || !seenInFile.Add(email)) { skippedEmails.Add(email); continue; }

            // A blank Role cell defaults to "User" (unchanged). A non-blank
            // Role that doesn't match a known value is kept as-is (not
            // silently coerced to "User") so the account gets no data access
            // (see StoreAccessService) and the typo stays visible to Admin.
            var matched = UserManagementPolicy.NormalizeRole(roleRaw);
            var role = matched ?? (string.IsNullOrWhiteSpace(roleRaw) ? "User" : roleRaw);

            // A non-Super-Admin uploader can't create Admin accounts this way
            // either — same rule as manual creation. Nothing is added to
            // toAdd yet and nothing has been persisted, so recording the row
            // here and rejecting the whole file below (before any
            // AddRangeAsync/SaveChangesAsync) keeps this atomic: either every
            // valid row is created, or none are.
            if (UserManagementPolicy.IsAdminRole(role) && !UserManagementPolicy.CanAssignRole(actorEmail, role))
            {
                forbiddenAdminRows.Add(row.RowNumber());
                continue;
            }

            toAdd.Add(new User { Email = email, Phone = phone, AssignedName = assignedName, Role = role, PasswordHash = null });
        }

        if (forbiddenAdminRows.Count > 0)
            throw new BulkUploadRoleForbiddenException(forbiddenAdminRows);

        if (toAdd.Count > 0)
        {
            await _db.Users.AddRangeAsync(toAdd);
            await _db.SaveChangesAsync();
        }

        // Warn (don't block) when a created user's Role doesn't match any
        // store's email column for that role in the latest Store Reference —
        // the most common bulk-upload mistake is a Role that doesn't match
        // the person's real assignment, which silently grants them zero
        // store access rather than failing loudly.
        var roleMismatches = new List<string>();
        foreach (var u in toAdd)
        {
            if (!_storeAccess.IsRestrictedRole(u.Role)) continue;
            var stores = await _storeAccess.GetAccessibleStoreNamesAsync(u.Role, u.Email);
            if (stores == null || stores.Count == 0)
                roleMismatches.Add($"{u.Email} ({u.Role})");
        }

        return (toAdd.Count, skippedEmails, roleMismatches);
    }
}
