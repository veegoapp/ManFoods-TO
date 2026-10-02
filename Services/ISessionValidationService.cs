namespace MvcApp.Services;

/// <summary>
/// Confirms a session's cached UserId/Role/password stamp still match the database, so a
/// deleted user, a changed Role or a changed password can't keep working off stale session
/// data for the rest of the session's lifetime.
/// </summary>
public interface ISessionValidationService
{
    /// <summary>True if the user still exists, their current database Role equals <paramref name="role"/>,
    /// and <paramref name="passwordFingerprint"/> (see <see cref="PasswordFingerprint"/>) still matches their
    /// current password — so a password change or reset ends every session opened before it.</summary>
    Task<bool> IsValidAsync(int userId, string role, string? passwordFingerprint);

    /// <summary>Forces the next <see cref="IsValidAsync"/> call for this user to hit the database
    /// instead of a cached result — called right after a user is edited, deleted or has their
    /// password changed so the change takes effect immediately instead of waiting out the cache window.</summary>
    void Invalidate(int userId);
}
