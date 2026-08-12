namespace MvcApp.Services;

/// <summary>
/// Confirms a session's cached UserId/Role still match the database, so a
/// deleted user or a changed Role can't keep working off stale session data
/// for the rest of the session's lifetime.
/// </summary>
public interface ISessionValidationService
{
    /// <summary>True if the user still exists and their current database Role equals <paramref name="role"/>.</summary>
    Task<bool> IsValidAsync(int userId, string role);

    /// <summary>Forces the next <see cref="IsValidAsync"/> call for this user to hit the database
    /// instead of a cached result — called right after an admin edits or deletes a user so the
    /// change takes effect immediately instead of waiting out the cache window.</summary>
    void Invalidate(int userId);
}
