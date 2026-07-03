using System.Security.Cryptography;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    private static UserViewModel ToVm(User u) => new()
    {
        Id = u.Id, Email = u.Email, Phone = u.Phone, Role = u.Role,
        HasPassword = !string.IsNullOrEmpty(u.PasswordHash), CreatedAt = u.CreatedAt
    };

    public async Task<List<UserViewModel>> GetAllAsync() =>
        (await _db.Users.OrderBy(u => u.CreatedAt).ToListAsync()).Select(ToVm).ToList();

    public async Task<UserViewModel?> GetByIdAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        return u == null ? null : ToVm(u);
    }

    public async Task<UserViewModel> CreateAsync(CreateUserViewModel vm)
    {
        var user = new User
        {
            Email = vm.Email.ToLower(),
            Phone = vm.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            Role = vm.Role,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return ToVm(user);
    }

    public async Task<UserViewModel?> UpdateAsync(int id, EditUserViewModel vm)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return null;
        user.Email = vm.Email.ToLower();
        user.Phone = vm.Phone;
        user.Role = vm.Role;
        if (!string.IsNullOrEmpty(vm.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password);
        await _db.SaveChangesAsync();
        return ToVm(user);
    }

    public async Task DeleteAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null) { _db.Users.Remove(user); await _db.SaveChangesAsync(); }
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string key)
    {
        var setting = await _db.AppSettings.FindAsync("admin_recovery_key_hash");
        if (setting == null || string.IsNullOrEmpty(setting.Value)) return false;
        return BCrypt.Net.BCrypt.Verify(key, setting.Value);
    }

    public async Task<bool> ResetAdminPasswordAsync(string email, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.Role == "Admin");
        if (user == null) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateRecoveryKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public async Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password)
    {
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

    public async Task<(int created, int skipped)> UploadBulkUsersAsync(IFormFile file)
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
        int skipped = 0;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var email = Col(row, ws, "Email", "email").ToLower();
            var phone = Col(row, ws, "Phone", "Phone Number", "phone");
            if (string.IsNullOrWhiteSpace(email)) continue;

            // Skip accounts that already exist (by email) so re-uploading a
            // file never overwrites an activated user, and skip in-file
            // duplicates too.
            if (existingEmails.Contains(email) || !seenInFile.Add(email)) { skipped++; continue; }

            toAdd.Add(new User { Email = email, Phone = phone, Role = "User", PasswordHash = null });
        }

        if (toAdd.Count > 0)
        {
            await _db.Users.AddRangeAsync(toAdd);
            await _db.SaveChangesAsync();
        }

        return (toAdd.Count, skipped);
    }
}
