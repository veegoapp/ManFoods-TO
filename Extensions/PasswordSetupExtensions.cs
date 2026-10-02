using Microsoft.Extensions.Caching.Memory;
using MvcApp.Models;

namespace MvcApp.Extensions;

/// <summary>
/// One-time tokens bridging "typed the correct temporary password" to
/// "actually set a new one" without ever establishing an authenticated
/// session in between. The forced first-login password update is shown as a
/// popup on the login page itself (not a page inside the portal), and
/// deliberately does not log the user in on success — they sign in fresh
/// with their new password afterward. Mirrors SessionExtensions'
/// BeginSessionRotation/CompleteSessionRotation pending-token pattern.
/// </summary>
public static class PasswordSetupExtensions
{
    private const string Prefix = "pending-password-setup:";
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    public static string BeginPasswordSetup(this IMemoryCache cache, User user)
    {
        var token = SecureToken.Create();
        cache.Set(Prefix + token, (user.Id, user.Email), Expiry);
        return token;
    }

    /// <summary>Consumes the token (single use) if it's still valid.</summary>
    public static bool TryCompletePasswordSetup(this IMemoryCache cache, string? token, out int userId, out string email)
    {
        userId = 0;
        email = "";
        if (string.IsNullOrEmpty(token)) return false;
        var key = Prefix + token;
        if (!cache.TryGetValue(key, out (int Id, string Email) pending)) return false;
        cache.Remove(key);
        userId = pending.Id;
        email = pending.Email;
        return true;
    }
}
