using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IUserService
{
    Task<List<UserViewModel>> GetAllAsync();
    Task<UserViewModel?> GetByIdAsync(int id);
    Task<UserViewModel> CreateAsync(CreateUserViewModel vm);
    Task<UserViewModel?> UpdateAsync(int id, EditUserViewModel vm);
    Task DeleteAsync(int id);
    Task<(int created, int skipped)> UploadBulkUsersAsync(IFormFile file);
    Task<bool> VerifyRecoveryKeyAsync(string key);
    Task<bool> ResetAdminPasswordAsync(string email, string newPassword);
    /// <summary>Regenerates the shared admin recovery key after verifying the
    /// requesting admin's own password. Returns the new plaintext key (shown
    /// once) or null if the password didn't match.</summary>
    Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password);
}
