using Microsoft.Extensions.Caching.Memory;

namespace MvcApp.Services;

/// <summary>Company-wide share (%) of hires who left within 90 days, over the latest finished
/// hire cohorts. One number for the whole company, cached for an hour.</summary>
public static class EarlyLeaverRate
{
    private const string CacheKey = "planning:early-leaver-rate";

    public static async Task<double> GetAsync(INinetyDayTurnoverService? ninetyDay, IMemoryCache? cache)
    {
        if (ninetyDay == null) return 0;
        if (cache != null && cache.TryGetValue(CacheKey, out double cached)) return cached;
        var trend = (await ninetyDay.GetTrendAsync(null, "Admin", null)).Where(t => !t.IsProvisional).TakeLast(6).ToList();
        var hires = trend.Sum(t => t.TotalHires);
        var rate = hires > 0 ? trend.Sum(t => t.EarlyLeavers) * 100.0 / hires : 0;
        cache?.Set(CacheKey, rate, TimeSpan.FromHours(1));
        return rate;
    }
}
