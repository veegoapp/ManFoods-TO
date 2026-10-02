using System.Security.Cryptography;
using System.Text;

namespace MvcApp.Services;

/// <summary>
/// A short, non-reversible stamp of an account's current password hash. A session
/// carries the stamp it was created with; SessionValidationService compares it to
/// the database on every request, so any password change or reset (BCrypt salts
/// every hash, so the stored hash always changes) invalidates every session that
/// was opened before it — with no extra database column.
/// </summary>
public static class PasswordFingerprint
{
    public static string Compute(string? passwordHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? ""));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    public static bool Matches(string? expected, string? actual) =>
        expected != null && actual != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
