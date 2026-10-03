using System.Security.Cryptography;
using System.Text;

namespace MvcApp.Services;

/// <summary>
/// The part of a cache key that says WHICH stores the current caller may see. Role + email is not enough:
/// a restricted role's view is widened (all stores) or kept to its own stores depending on the endpoint's
/// access area and the admin's per-area configuration (see StoreAccessService), so the same user can get
/// different rows for the same filters from two endpoints. Keying on the resolved store list keeps those
/// results apart — two callers share a cached entry only when they are allowed to see exactly the same stores.
/// </summary>
public static class AccessScopeKey
{
    /// <summary>"ALL" for unrestricted access (null list), otherwise a short fingerprint of the accessible store set.</summary>
    public static string Of(IReadOnlyCollection<string>? accessibleStores)
    {
        if (accessibleStores == null) return "ALL";
        // Order- and case-insensitive set, same as the store comparisons in the services (SQL is case-insensitive by default).
        var canonical = string.Join('\u001f', accessibleStores
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal));
        return "S:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
    }

    /// <summary>Resolves the caller's accessible stores for the CURRENT request (area-aware) and fingerprints them.</summary>
    public static async Task<string> ForAsync(IStoreAccessService storeAccess, string role, string? email) =>
        Of(await storeAccess.GetAccessibleStoreNamesAsync(role, email));
}
