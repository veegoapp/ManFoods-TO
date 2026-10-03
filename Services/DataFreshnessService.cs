using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;

namespace MvcApp.Services;

public sealed class DataFreshnessService : IDataFreshnessService
{
    private static readonly string[] PeriodFileTypes =
    {
        "active_employees",
        "resignations",
        "store_reference"
    };

    // The layout asks for this on every page open, so it is cached (the answer only changes when a
    // period file is uploaded/deleted — UploadService calls InvalidateCache()). The wrapper lets a
    // "no data yet" (null) result be cached too.
    private const string CacheKey = "data-freshness:latest-period";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static int _version;

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public DataFreshnessService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public static void InvalidateCache() => Interlocked.Increment(ref _version);

    public async Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync()
    {
        var key = CacheKey + ":" + Volatile.Read(ref _version);
        if (_cache.TryGetValue(key, out DataFreshnessPeriod?[]? cached) && cached is { Length: 1 })
            return cached[0];

        var latest = await _db.UploadLogs
            .AsNoTracking()
            .Where(log => PeriodFileTypes.Contains(log.FileType))
            .OrderByDescending(log => log.Year)
            .ThenByDescending(log => log.Month)
            .Select(log => new { log.Month, log.Year })
            .FirstOrDefaultAsync();

        var result = latest is null ? null : new DataFreshnessPeriod(latest.Month, latest.Year);
        // One entry per version: an invalidation leaves the old one to expire on its own.
        _cache.Set(key, new[] { result }, CacheDuration);
        return result;
    }
}
