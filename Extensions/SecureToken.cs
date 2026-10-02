using System.Security.Cryptography;

namespace MvcApp.Extensions;

/// <summary>
/// Unguessable one-time token values (session-rotation and password-setup
/// tokens). Backed by the OS CSPRNG — Guid.NewGuid() is only guaranteed unique,
/// not unpredictable, so it must not be used where the value is a bearer secret.
/// 32 random bytes → 64 lowercase hex characters (URL/cookie/JSON safe).
/// </summary>
public static class SecureToken
{
    public const int ByteLength = 32;

    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(ByteLength)).ToLowerInvariant();
}
