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

    public const string LatestPeriodCacheKey = "data-freshness:latest-period";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public DataFreshnessService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // Read by the layout on every page; cached so it costs one DB request per upload, not per page.
    // The wrapper lets "no uploads yet" (null) be cached too.
    public async Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync()
    {
        if (_cache.TryGetValue(LatestPeriodCacheKey, out Cached? hit) && hit != null)
            return hit.Period;

        var period = await LoadLatestDataPeriodAsync();
        _cache.Set(LatestPeriodCacheKey, new Cached(period), TimeSpan.FromMinutes(10));
        return period;
    }

    private sealed record Cached(DataFreshnessPeriod? Period);

    private async Task<DataFreshnessPeriod?> LoadLatestDataPeriodAsync()
    {
        var latest = await _db.UploadLogs
            .AsNoTracking()
            .Where(log => PeriodFileTypes.Contains(log.FileType))
            .OrderByDescending(log => log.Year)
            .ThenByDescending(log => log.Month)
            .Select(log => new { log.Month, log.Year })
            .FirstOrDefaultAsync();

        return latest is null ? null : new DataFreshnessPeriod(latest.Month, latest.Year);
    }
}