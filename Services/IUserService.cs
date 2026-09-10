using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IUserService
{
    /// <summary>The full set of role values the app recognizes — the same
    /// list Bulk User Upload validates a Role cell against, and what the
    /// downloadable template's Role-column dropdown offers.</summary>
    IReadOnlyList<string> ValidRoles { get; }

    /// <summary>Users visible to `actorEmail` per UserManagementPolicy.CanView —
    /// a normal Admin never sees another Admin or the Super Admin row.</summary>
    Task<List<UserViewModel>> GetAllAsync(string actorEmail);
    /// <summary>Returns null if the user doesn't exist, or if `actorEmail` is
    /// not allowed to view it (UserManagementPolicy.CanView) — both cases look
    /// identical to the caller so a hidden Admin/Super-Admin id can't be
    /// distinguished from a nonexistent one.</summary>
    Task<UserViewModel?> GetByIdAsync(int id, string actorEmail);
    /// <summary>Returns null under the same visibility rule as GetByIdAsync
    /// (not found, or `actorEmail` isn't allowed to view this user). Logins
    /// are most-recent-first, capped at 200 rows.</summary>
    Task<UserLoginHistoryViewModel?> GetLoginHistoryAsync(int id, string actorEmail);
    /// <summary>Returns (null, "duplicate-email", null) when a user with this
    /// email already exists, (null, "invalid-role", null) when Role isn't one
    /// of ValidRoles, or (null, "role-forbidden", null) when `actorEmail`
    /// isn't allowed to assign that role (UserManagementPolicy.CanAssignRole —
    /// only the Super Admin may create an Admin account). On success, a
    /// compliant temporary password (PasswordPolicy) is generated and
    /// returned once as plaintext — the caller must show it so it can be
    /// sent to the new user — and the account is created with
    /// MustChangePassword set, forcing them to replace it on first login.</summary>
    Task<(UserViewModel? user, string? error, string? temporaryPassword)> CreateAsync(CreateUserViewModel vm, string actorEmail);
    /// <summary>Returns (null, error) when the update is rejected — the user
    /// wasn't found or isn't visible to `actorEmail` (null error, same as
    /// not-found), "invalid-role" (Role isn't one of ValidRoles),
    /// "role-forbidden" (actor isn't allowed to change to that role — includes
    /// self-promotion and changing the Super Admin's own Role), "last-admin"
    /// (this is the last remaining Admin and the edit would take away their
    /// Admin role), "super-admin-protected" (attempting to change the Super
    /// Admin's email), or "duplicate-email".</summary>
    Task<(UserViewModel? user, string? error)> UpdateAsync(int id, EditUserViewModel vm, string actorEmail);
    /// <summary>Returns (false, null) when the user doesn't exist or isn't
    /// deletable by `actorEmail` (UserManagementPolicy.CanDelete — includes a
    /// normal Admin targeting any Admin/Super-Admin account, indistinguishable
    /// from not-found), (false, "super-admin-protected") only when the Super
    /// Admin itself attempts to delete its own account, or (false,
    /// "last-admin") for the last remaining Admin account.</summary>
    Task<(bool success, string? error)> DeleteAsync(int id, string actorEmail);
    /// <summary>Rows whose Role would resolve to Admin are silently skipped
    /// (counted in `skipped`, never created) unless `actorEmail` is the Super
    /// Admin — same rule as manual creation, applied per-row so a manipulated
    /// upload file can't create Admin accounts either. `roleMismatches` lists
    /// "email (Role)" for every created, store-restricted-role user whose
    /// email doesn't appear under that role's column in the latest uploaded
    /// Store Reference — a likely wrong Role on that row — as a warning only;
    /// those accounts are still created.</summary>
    Task<(int created, int skipped, IReadOnlyList<string> roleMismatches)> UploadBulkUsersAsync(IFormFile file, string actorEmail);
    Task<bool> VerifyRecoveryKeyAsync(string key);
    Task<bool> ResetAdminPasswordAsync(string email, string newPassword);
    /// <summary>Regenerates the shared admin recovery key after verifying the
    /// requesting admin's own password. Returns the new plaintext key (shown
    /// once) or null if the password didn't match.</summary>
    Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password);
}
