using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace MvcApp.Services;

/// <summary>
/// Cache for results that are keyed by what a caller typed into a filter (store / OM / OC / job /
/// month lists...). The application-wide IMemoryCache has no size limit, so a signed-in client
/// could grow it without bound just by varying a query-string value. Results keyed by filter values
/// therefore live here instead: a separate cache with a hard size limit (entries are weighted — a
/// list result costs more than a scalar), short TTLs set by the caller, and the usual eviction when
/// full. Normal use fits comfortably; abuse can only ever occupy this bounded space and evict other
/// filter results, never the fixed-key caches or the process memory.
/// </summary>
public sealed class FilterResultCache
{
    /// <summary>Total weight the cache may hold. A scalar costs 1 and a list 1 + count/10, so e.g.
    /// ~100 watch-lists of 5,000 employees, or many thousands of small results.</summary>
    public const long DefaultSizeLimit = 50_000;

    private readonly MemoryCache _cache;

    // Bumped (InvalidateAll) whenever data or access rules change — UploadService on uploads/deletes,
    // AccessPolicyService when the Super Admin saves the access policy. KeyFor() folds the current version into
    // the key, so every result computed under the old data/rules stops matching at once (it simply ages out of
    // the bounded cache) instead of being served until its TTL ends. The key is built BEFORE the result is
    // loaded, so a load that straddles an invalidation is stored under the OLD version and never served after it.
    // The version is per instance (this class is a DI singleton), so tests with their own cache don't interfere.
    private int _version;
    public void InvalidateAll() => Interlocked.Increment(ref _version);

    /// <summary>Like <see cref="BuildKey"/>, plus this cache's current data/access-rules version.</summary>
    public string KeyFor(string prefix, params object?[] parts) =>
        BuildKey(prefix + "@v" + Volatile.Read(ref _version), parts);

    public FilterResultCache(long sizeLimit = DefaultSizeLimit) =>
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit });

    public int Count => _cache.Count;

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out T? found) && found != null) { value = found; return true; }
        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl, int count = 0) =>
        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1 + Math.Max(0, count) / 10,
        });

    // ── key building ─────────────────────────────────────────────────────────

    /// <summary>
    /// Canonical form of a comma-separated filter value: trimmed, empty entries dropped, duplicates
    /// removed and sorted — so "B, A" / "A,B,A" / " a,b" style variants of the same selection share one
    /// entry instead of each creating a new one. Case is left alone (some comparisons are
    /// case-sensitive, so two spellings are not necessarily the same filter). The services read these
    /// values as sets (see MultiValueFilter), so the canonical form selects exactly the same rows.
    /// </summary>
    public static string NormalizeList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return "";
        var values = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal);
        return string.Join(',', values);
    }

    /// <summary>
    /// Canonical form of a "months" list: only the valid month numbers 1–12, distinct and sorted —
    /// exactly what DashboardService.ResolvePeriods keeps of it (anything else is ignored there), so
    /// requests that resolve to the same months share one entry.
    /// </summary>
    public static string NormalizeMonths(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return "";
        var months = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var m) ? m : (int?)null)
            .Where(m => m is >= 1 and <= 12)
            .Select(m => m!.Value)
            .Distinct()
            .OrderBy(m => m);
        return string.Join(',', months);
    }

    /// <summary>A fixed-length key from a prefix and the key parts, so the key size no longer depends on how long
    /// the request's filter values were. The current UI culture is folded in too: cached
    /// results carry localized labels, so a language switch must not be served the other language's entry.</summary>
    public static string BuildKey(string prefix, params object?[] parts)
    {
        var joined = CultureInfo.CurrentUICulture.Name + "\u001f" + string.Join('\u001f', parts.Select(p => p?.ToString() ?? ""));
        return prefix + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }
}
