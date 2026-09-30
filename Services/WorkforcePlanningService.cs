using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class WorkforcePlanningService : IWorkforcePlanningService
{
    // Fill % (actual / projected) thresholds for the status colours.
    private const double OkFill = 95.0;
    private const double WatchFill = 85.0;

    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;

    public WorkforcePlanningService(AppDbContext db, IStoreAccessService storeAccess, IMemoryCache cache)
    {
        _db = db;
        _storeAccess = storeAccess;
        _cache = cache;
    }

    // Every cached read below hangs off one change token, so a single call drops
    // them all when active employees or job projections change (uploads/deletes).
    private static CancellationTokenSource _reset = new();
    public static void InvalidateCache() => Interlocked.Exchange(ref _reset, new CancellationTokenSource()).Cancel();

    private static MemoryCacheEntryOptions CacheOptions() =>
        new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(_reset.Token))
            .SetAbsoluteExpiration(TimeSpan.FromHours(6));

    // One compact cell = (month, store, job, count). Store/job names live once in
    // the shared tables, so a full year (~65k cells) stays around a megabyte.
    private readonly record struct Cell(int Month, int Store, int Job, int Count);

    private sealed class YearData
    {
        public List<string> Stores { get; } = new();
        public List<string> Jobs { get; } = new();
        public Cell[] Projected { get; set; } = Array.Empty<Cell>();
        public Cell[] Actual { get; set; } = Array.Empty<Cell>();
    }

    private async Task<List<int>> GetYearsAsync()
    {
        const string key = "planning:years";
        if (_cache.TryGetValue(key, out List<int>? cached) && cached != null) return cached;
        var years = await _db.JobHeadcountProjections.AsNoTracking()
            .Select(j => j.Year).Distinct().OrderByDescending(y => y).ToListAsync();
        _cache.Set(key, years, CacheOptions());
        return years;
    }

    private async Task<YearData> GetYearDataAsync(int year)
    {
        var key = $"planning:year:{year}";
        if (_cache.TryGetValue(key, out YearData? cached) && cached != null) return cached;

        var data = new YearData();
        var storeIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var jobIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Store(string s) { s = (s ?? "").Trim(); if (!storeIdx.TryGetValue(s, out var i)) { i = data.Stores.Count; data.Stores.Add(s); storeIdx[s] = i; } return i; }
        int Job(string s) { s = (s ?? "").Trim(); if (!jobIdx.TryGetValue(s, out var i)) { i = data.Jobs.Count; data.Jobs.Add(s); jobIdx[s] = i; } return i; }

        var proj = await _db.JobHeadcountProjections.AsNoTracking()
            .Where(j => j.Year == year)
            .Select(j => new { j.Month, j.StoreName, j.JobTitle, j.ProjectedHeadcount })
            .ToListAsync();
        data.Projected = proj.Select(p => new Cell(p.Month, Store(p.StoreName), Job(p.JobTitle), p.ProjectedHeadcount)).ToArray();

        var actual = await _db.ActiveEmployees.AsNoTracking()
            .Where(e => e.Year == year)
            .GroupBy(e => new { e.Month, e.Store, e.JobTitle })
            .Select(g => new { g.Key.Month, g.Key.Store, g.Key.JobTitle, Count = g.Count() })
            .ToListAsync();
        data.Actual = actual.Select(a => new Cell(a.Month, Store(a.Store), Job(a.JobTitle), a.Count)).ToArray();

        _cache.Set(key, data, CacheOptions());
        return data;
    }

    public async Task<WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName)
    {
        var dto = new WorkforcePlanningDto();
        var years = await GetYearsAsync();
        dto.Years = years;
        if (years.Count == 0) return dto;
        dto.HasData = true;

        var now = DateTime.Now;
        var y = year.HasValue && years.Contains(year.Value) ? year.Value : (years.Contains(now.Year) ? now.Year : years[0]);
        dto.Year = y;
        var data = await GetYearDataAsync(y);

        // Store scope: what this caller may see (null = everything), narrowed by the filter.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var accessibleSet = accessible == null ? null : new HashSet<string>(accessible.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var storeFilter = MultiValueFilter.Split(stores);
        var storeFilterSet = storeFilter == null ? null : new HashSet<string>(storeFilter, StringComparer.OrdinalIgnoreCase);
        var jobFilter = MultiValueFilter.Split(jobs);
        var jobFilterSet = jobFilter == null ? null : new HashSet<string>(jobFilter, StringComparer.OrdinalIgnoreCase);

        bool StoreOk(int i) =>
            (accessibleSet == null || accessibleSet.Contains(data.Stores[i])) &&
            (storeFilterSet == null || storeFilterSet.Contains(data.Stores[i]));
        bool JobOk(int j) => jobFilterSet == null || jobFilterSet.Contains(data.Jobs[j]);

        // Filter dropdown options: only what the caller can see in this year's projection.
        dto.Stores = data.Projected.Select(c => c.Store).Distinct().Where(i => accessibleSet == null || accessibleSet.Contains(data.Stores[i]))
            .Select(i => data.Stores[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        dto.Jobs = data.Projected.Select(c => c.Job).Distinct().Select(i => data.Jobs[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        var projMonths = data.Projected.Select(c => c.Month).Distinct().OrderBy(m => m).ToList();
        var actualMonths = data.Actual.Select(c => c.Month).Distinct().ToHashSet();
        dto.Months = projMonths;
        var m = month.HasValue && projMonths.Contains(month.Value) ? month.Value
              : (y == now.Year && projMonths.Contains(now.Month)) ? now.Month
              : actualMonths.Intersect(projMonths).DefaultIfEmpty(projMonths.Count > 0 ? projMonths[^1] : 1).Max();
        dto.Month = m;
        dto.HasActual = actualMonths.Contains(m);

        // For a month, only stores that actually have projection rows are compared,
        // so a store nobody planned for never shows up as a "surplus".
        (Dictionary<int, int[]> ByStore, Dictionary<int, int[]> ByJob, int Projected, int? Actual) Aggregate(int mon)
        {
            var planned = new HashSet<int>(data.Projected.Where(c => c.Month == mon && StoreOk(c.Store)).Select(c => c.Store));
            var byStore = new Dictionary<int, int[]>(); // [projected, actual]
            var byJob = new Dictionary<int, int[]>();
            int p = 0, a = 0;
            foreach (var c in data.Projected)
            {
                if (c.Month != mon || !planned.Contains(c.Store) || !JobOk(c.Job)) continue;
                p += c.Count;
                Bump(byStore, c.Store, 0, c.Count); Bump(byJob, c.Job, 0, c.Count);
            }
            bool hasActual = actualMonths.Contains(mon);
            if (hasActual)
            {
                foreach (var c in data.Actual)
                {
                    if (c.Month != mon || !planned.Contains(c.Store) || !JobOk(c.Job)) continue;
                    a += c.Count;
                    Bump(byStore, c.Store, 1, c.Count); Bump(byJob, c.Job, 1, c.Count);
                }
            }
            return (byStore, byJob, p, hasActual ? a : null);
        }

        var sel = Aggregate(m);
        dto.ByStore = sel.ByStore.Select(kv => Row(data.Stores[kv.Key], kv.Value, dto.HasActual))
            .OrderByDescending(r => dto.HasActual ? r.Gap : r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        dto.ByJob = sel.ByJob.Select(kv => Row(data.Jobs[kv.Key], kv.Value, dto.HasActual))
            .OrderByDescending(r => r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var actualTotal = sel.Actual ?? 0;
        dto.Kpis = new PlanningKpiDto
        {
            Projected = sel.Projected,
            Actual = actualTotal,
            Gap = sel.Projected - actualTotal,
            FillPercent = sel.Projected > 0 ? Math.Round(actualTotal * 100.0 / sel.Projected, 1) : 0,
            StoresCount = dto.ByStore.Count,
            StoresShort = dto.HasActual ? dto.ByStore.Count(r => r.Status is "watch" or "critical") : 0,
        };

        foreach (var mon in projMonths)
        {
            var t = Aggregate(mon);
            dto.Trend.Add(new PlanningTrendPointDto { Month = mon, Projected = t.Projected, Actual = t.Actual });
        }
        return dto;
    }

    private static void Bump(Dictionary<int, int[]> map, int key, int slot, int by)
    {
        if (!map.TryGetValue(key, out var arr)) map[key] = arr = new int[2];
        arr[slot] += by;
    }

    private static PlanningRowDto Row(string name, int[] pa, bool hasActual)
    {
        var p = pa[0]; var a = hasActual ? pa[1] : 0;
        var fill = p > 0 ? Math.Round(a * 100.0 / p, 1) : 0;
        return new PlanningRowDto
        {
            Name = name, Projected = p, Actual = a, Gap = p - a, FillPercent = fill,
            Status = !hasActual ? "none" : p == 0 ? "ok" : fill >= OkFill ? "ok" : fill >= WatchFill ? "watch" : "critical",
        };
    }
}
