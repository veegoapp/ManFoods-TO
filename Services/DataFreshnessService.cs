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
    public async Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync() => (await GetOrLoadAsync()).Period;

    public async Task<DateTime?> GetLatestUploadUtcAsync() => (await GetOrLoadAsync()).UploadedAtUtc;

    private async Task<Cached> GetOrLoadAsync()
    {
        if (_cache.TryGetValue(LatestPeriodCacheKey, out Cached? hit) && hit != null)
            return hit;

        var loaded = await LoadLatestAsync();
        _cache.Set(LatestPeriodCacheKey, loaded, TimeSpan.FromMinutes(10));
        return loaded;
    }

    private sealed record Cached(DataFreshnessPeriod? Period, DateTime? UploadedAtUtc);

    private async Task<Cached> LoadLatestAsync()
    {
        var latest = await _db.UploadLogs
            .AsNoTracking()
            .Where(log => PeriodFileTypes.Contains(log.FileType))
            .OrderByDescending(log => log.Year)
            .ThenByDescending(log => log.Month)
            .Select(log => new { log.Month, log.Year })
            .FirstOrDefaultAsync();

        if (latest is null) return new Cached(null, null);

        // When the files of that latest period were uploaded (the newest of its logs).
        var uploadedAt = await _db.UploadLogs
            .AsNoTracking()
            .Where(log => PeriodFileTypes.Contains(log.FileType) && log.Year == latest.Year && log.Month == latest.Month)
            .MaxAsync(log => log.UploadDate);

        return new Cached(new DataFreshnessPeriod(latest.Month, latest.Year), uploadedAt);
    }
}
