using System.Text.RegularExpressions;
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

    // ── Expected resignations ──
    // Average monthly resignations per store+job over the last few roster months up to
    // the month being planned, from the uploaded Resignations files.
    private const int AttritionLookbackMonths = 6;

    private sealed class AttritionData
    {
        public Dictionary<string, double> PerMonth { get; } = new();
        public int Periods { get; set; }
    }

    private static string Norm(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ").ToLowerInvariant();
    private static string AttrKey(string store, string job) => Norm(store) + "\u001f" + Norm(job);

    // The people responsible for each store, from the Store Reference file: the entry for the
    // planned period if there is one, else the latest earlier entry, else the latest overall.
    private sealed record Leaders(string Oc, string Om, string Soc, string Od);

    private async Task<Dictionary<string, Leaders>> GetLeadershipAsync(int year, int month)
    {
        var key = $"planning:leaders:{year}:{month}";
        if (_cache.TryGetValue(key, out Dictionary<string, Leaders>? cached) && cached != null) return cached;

        var map = new Dictionary<string, Leaders>(StringComparer.OrdinalIgnoreCase);
        var target = year * 100 + month;
        var rows = await _db.StoreReferences.AsNoTracking()
            .Select(s => new { s.StoreName, s.OperationConsultant, s.OperationManager, s.SeniorOperationConsultant, s.OperationDirector, Period = s.Year * 100 + s.Month })
            .ToListAsync();
        foreach (var g in rows.GroupBy(r => r.StoreName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var pick = g.Where(r => r.Period == target).FirstOrDefault()
                    ?? g.Where(r => r.Period < target).OrderByDescending(r => r.Period).FirstOrDefault()
                    ?? g.OrderByDescending(r => r.Period).First();
            map[g.Key] = new Leaders((pick.OperationConsultant ?? "").Trim(), (pick.OperationManager ?? "").Trim(),
                (pick.SeniorOperationConsultant ?? "").Trim(), (pick.OperationDirector ?? "").Trim());
        }
        _cache.Set(key, map, CacheOptions());
        return map;
    }

    // Comma-separated leadership filters -> case-insensitive sets (null = not filtering on that role).
    private static HashSet<string>? LeaderSet(string? csv)
    {
        var list = MultiValueFilter.Split(csv);
        return list == null ? null : new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }

    private static bool LeadersMatch(Leaders? l, HashSet<string>? om, HashSet<string>? oc, HashSet<string>? soc, HashSet<string>? od)
    {
        if (om == null && oc == null && soc == null && od == null) return true;
        if (l == null) return false; // a store with no Store Reference entry can't match a leadership filter
        return (om == null || om.Contains(l.Om)) && (oc == null || oc.Contains(l.Oc))
            && (soc == null || soc.Contains(l.Soc)) && (od == null || od.Contains(l.Od));
    }

    private async Task<List<int>> GetRosterPeriodKeysAsync()
    {
        const string key = "planning:roster-periods";
        if (_cache.TryGetValue(key, out List<int>? cached) && cached != null) return cached;
        var keys = await _db.ActiveEmployees.AsNoTracking().Select(e => e.Year * 100 + e.Month).Distinct().OrderByDescending(k => k).ToListAsync();
        _cache.Set(key, keys, CacheOptions());
        return keys;
    }

    private async Task<AttritionData> GetAttritionAsync(int year, int month)
    {
        var key = $"planning:attrition:{year}:{month}";
        if (_cache.TryGetValue(key, out AttritionData? cached) && cached != null) return cached;

        var data = new AttritionData();
        var used = (await GetRosterPeriodKeysAsync()).Where(k => k <= year * 100 + month).Take(AttritionLookbackMonths).ToList();
        data.Periods = used.Count;
        if (used.Count > 0)
        {
            var rows = await _db.Resignations.AsNoTracking()
                .Where(r => used.Contains(r.Year * 100 + r.Month))
                .GroupBy(r => new { r.Store, r.JobTitle })
                .Select(g => new { g.Key.Store, g.Key.JobTitle, Count = g.Count() })
                .ToListAsync();
            foreach (var r in rows)
            {
                var k = AttrKey(r.Store, r.JobTitle);
                data.PerMonth[k] = (data.PerMonth.TryGetValue(k, out var prev) ? prev : 0) + r.Count / (double)used.Count;
            }
        }
        _cache.Set(key, data, CacheOptions());
        return data;
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

    public Task<WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        BuildAsync(year, month, stores, jobs, role, assignedName, strict: false, includeTrend: true, includeHiring: true, om: om, oc: oc, soc: soc, od: od);

    // strict: use exactly the requested year/month or return "no data" (the page itself
    // falls back to a sensible default period instead).
    private async Task<WorkforcePlanningDto> BuildAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName, bool strict, bool includeTrend, bool includeHiring = false,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var dto = new WorkforcePlanningDto();
        var years = await GetYearsAsync();
        dto.Years = years;
        if (years.Count == 0) return dto;
        if (strict && (!year.HasValue || !years.Contains(year.Value) || !month.HasValue)) return dto;
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

        var omSet = LeaderSet(om); var ocSet = LeaderSet(oc); var socSet = LeaderSet(soc); var odSet = LeaderSet(od);
        var leaders = new Dictionary<string, Leaders>(StringComparer.OrdinalIgnoreCase); // filled once the month is known
        bool StoreOk(int i) =>
            (accessibleSet == null || accessibleSet.Contains(data.Stores[i])) &&
            (storeFilterSet == null || storeFilterSet.Contains(data.Stores[i])) &&
            LeadersMatch(leaders.TryGetValue(data.Stores[i].Trim(), out var lead) ? lead : null, omSet, ocSet, socSet, odSet);
        bool JobOk(int j) => jobFilterSet == null || jobFilterSet.Contains(data.Jobs[j]);

        // Filter dropdown options: only what the caller can see in this year's projection.
        dto.Stores = data.Projected.Select(c => c.Store).Distinct().Where(i => accessibleSet == null || accessibleSet.Contains(data.Stores[i]))
            .Select(i => data.Stores[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        dto.Jobs = data.Projected.Select(c => c.Job).Distinct().Select(i => data.Jobs[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        var projMonths = data.Projected.Select(c => c.Month).Distinct().OrderBy(m => m).ToList();
        var actualMonths = data.Actual.Select(c => c.Month).Distinct().ToHashSet();
        dto.Months = projMonths;
        if (strict && !projMonths.Contains(month!.Value)) { dto.HasData = false; return dto; }
        var m = month.HasValue && projMonths.Contains(month.Value) ? month.Value
              : (y == now.Year && projMonths.Contains(now.Month)) ? now.Month
              : actualMonths.Intersect(projMonths).DefaultIfEmpty(projMonths.Count > 0 ? projMonths[^1] : 1).Max();
        dto.Month = m;
        dto.HasActual = actualMonths.Contains(m);

        // The people responsible for each store this month: drives the leadership filters, their
        // dropdown options and the roll-up tables below.
        leaders = await GetLeadershipAsync(y, m);
        var visibleLeaders = data.Projected.Select(c => c.Store).Distinct()
            .Where(i => accessibleSet == null || accessibleSet.Contains(data.Stores[i]))
            .Select(i => leaders.TryGetValue(data.Stores[i].Trim(), out var vl) ? vl : null).Where(vl => vl != null).Select(vl => vl!).ToList();
        List<string> Options(Func<Leaders, string> pick) => visibleLeaders.Select(pick).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        dto.OperationConsultants = Options(l => l.Oc);
        dto.OperationManagers = Options(l => l.Om);
        dto.SeniorOperationConsultants = Options(l => l.Soc);
        dto.OperationDirectors = Options(l => l.Od);

        // For a month, only stores that actually have projection rows are compared,
        // so a store nobody planned for never shows up as a "surplus".
        (Dictionary<int, int[]> ByStore, Dictionary<int, int[]> ByJob, Dictionary<(int Store, int Job), int[]> Cells, int Projected, int? Actual) Aggregate(int mon)
        {
            var planned = new HashSet<int>(data.Projected.Where(c => c.Month == mon && StoreOk(c.Store)).Select(c => c.Store));
            var byStore = new Dictionary<int, int[]>(); // [projected, actual]
            var byJob = new Dictionary<int, int[]>();
            var cells = new Dictionary<(int Store, int Job), int[]>();
            int p = 0, a = 0;
            foreach (var c in data.Projected)
            {
                if (c.Month != mon || !planned.Contains(c.Store) || !JobOk(c.Job)) continue;
                p += c.Count;
                Bump(byStore, c.Store, 0, c.Count); Bump(byJob, c.Job, 0, c.Count); Bump(cells, (c.Store, c.Job), 0, c.Count);
            }
            bool hasActual = actualMonths.Contains(mon);
            if (hasActual)
            {
                foreach (var c in data.Actual)
                {
                    if (c.Month != mon || !planned.Contains(c.Store) || !JobOk(c.Job)) continue;
                    a += c.Count;
                    Bump(byStore, c.Store, 1, c.Count); Bump(byJob, c.Job, 1, c.Count); Bump(cells, (c.Store, c.Job), 1, c.Count);
                }
            }
            return (byStore, byJob, cells, p, hasActual ? a : null);
        }

        var sel = Aggregate(m);
        dto.ByStore = sel.ByStore.Select(kv => Row(data.Stores[kv.Key], kv.Value, dto.HasActual))
            .OrderByDescending(r => dto.HasActual ? r.Gap : r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        dto.ByJob = sel.ByJob.Select(kv => Row(data.Jobs[kv.Key], kv.Value, dto.HasActual))
            .OrderByDescending(r => r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var r in dto.ByStore) r.OperationConsultant = leaders.TryGetValue(r.Name.Trim(), out var l) ? l.Oc : "";

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

        // Hiring need: per store+job the shortage plus expected resignations, never below
        // zero (a surplus in one job can't cover another), summed and rounded for display.
        Dictionary<string, double[]>? storeNeed = null; // store -> [need, expected resignations], unrounded
        if (includeHiring && dto.HasActual)
        {
            var attrition = await GetAttritionAsync(y, m);
            dto.AttritionMonths = attrition.Periods;
            var byStoreNeed = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // [need, attrition]
            var byJobNeed = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            double totalNeed = 0, totalAttr = 0;
            foreach (var cell in sel.Cells)
            {
                var store = data.Stores[cell.Key.Store]; var job = data.Jobs[cell.Key.Job];
                var expected = attrition.PerMonth.TryGetValue(AttrKey(store, job), out var e) ? e : 0;
                var need = Math.Max(0, cell.Value[0] - cell.Value[1] + expected);
                AddNeed(byStoreNeed, store, need, expected); AddNeed(byJobNeed, job, need, expected);
                totalNeed += need; totalAttr += expected;
            }
            foreach (var r in dto.ByStore) if (byStoreNeed.TryGetValue(r.Name, out var v)) { r.HiringNeed = RoundNeed(v[0]); r.ExpectedAttrition = Math.Round(v[1], 1); }
            foreach (var r in dto.ByJob) if (byJobNeed.TryGetValue(r.Name, out var v)) { r.HiringNeed = RoundNeed(v[0]); r.ExpectedAttrition = Math.Round(v[1], 1); }
            dto.Kpis.HiringNeed = RoundNeed(totalNeed);
            dto.Kpis.ExpectedAttrition = Math.Round(totalAttr, 1);
            storeNeed = byStoreNeed;
        }

        // Roll the compared stores up by the people responsible for them. Figures are summed from
        // the stores (hiring need from the unrounded per-store values), so each table adds up.
        if (includeHiring)
        {
            List<PlanningRowDto> RollUp(Func<Leaders, string> pick)
            {
                var acc = new Dictionary<string, (int Stores, int P, int A, double Need, double Attr)>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in dto.ByStore)
                {
                    if (!leaders.TryGetValue(r.Name.Trim(), out var l)) continue;
                    var name = pick(l);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    acc.TryGetValue(name, out var a);
                    double need = 0, attr = 0;
                    if (storeNeed != null && storeNeed.TryGetValue(r.Name, out var sn)) { need = sn[0]; attr = sn[1]; }
                    acc[name] = (a.Stores + 1, a.P + r.Projected, a.A + (dto.HasActual ? r.Actual : 0), a.Need + need, a.Attr + attr);
                }
                return acc.Select(kv =>
                    {
                        var row = Row(kv.Key, new[] { kv.Value.P, kv.Value.A }, dto.HasActual);
                        row.StoreCount = kv.Value.Stores;
                        row.HiringNeed = RoundNeed(kv.Value.Need);
                        row.ExpectedAttrition = Math.Round(kv.Value.Attr, 1);
                        return row;
                    })
                    .OrderByDescending(r => dto.HasActual ? r.Gap : r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            dto.ByOperationConsultant = RollUp(l => l.Oc);
            dto.ByOperationDirector = RollUp(l => l.Od);
            dto.ByOperationManager = RollUp(l => l.Om);
            dto.BySeniorOperationConsultant = RollUp(l => l.Soc);
        }

        foreach (var mon in includeTrend ? projMonths : new List<int>())
        {
            var t = Aggregate(mon);
            dto.Trend.Add(new PlanningTrendPointDto { Month = mon, Projected = t.Projected, Actual = t.Actual });
        }
        return dto;
    }

    public async Task<List<StoreFillDto>> GetStoreFillAsync(int year, int month, string? jobs, string role, string? assignedName)
    {
        var dto = await BuildAsync(year, month, null, jobs, role, assignedName, strict: true, includeTrend: false);
        if (!dto.HasData) return new List<StoreFillDto>();
        return dto.ByStore.Select(r => new StoreFillDto
        {
            Store = r.Name, Projected = r.Projected, Actual = dto.HasActual ? r.Actual : null,
            FillPercent = r.FillPercent, Status = r.Status,
        }).ToList();
    }

    public async Task<StorePlanDto> GetStorePlanAsync(string store, int year, int month, string role, string? assignedName)
    {
        var plan = new StorePlanDto { Year = year, Month = month };
        if (string.IsNullOrWhiteSpace(store)) return plan;
        var dto = await BuildAsync(year, month, store, null, role, assignedName, strict: true, includeTrend: true);
        if (!dto.HasData || dto.ByStore.Count == 0) return plan;
        plan.HasData = true;
        plan.HasActual = dto.HasActual;
        plan.Kpis = dto.Kpis;
        plan.ByJob = dto.ByJob;
        plan.Upcoming = dto.Trend.Where(t => t.Month >= month).OrderBy(t => t.Month).Take(4).ToList();
        return plan;
    }

    public async Task<List<PlanningDetailRow>> GetDetailAsync(int year, IReadOnlyCollection<int>? months, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var rows = new List<PlanningDetailRow>();
        if (!(await GetYearsAsync()).Contains(year)) return rows;
        var data = await GetYearDataAsync(year);

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var accessibleSet = accessible == null ? null : new HashSet<string>(accessible.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var storeFilter = MultiValueFilter.Split(stores);
        var storeSet = storeFilter == null ? null : new HashSet<string>(storeFilter, StringComparer.OrdinalIgnoreCase);
        var jobFilter = MultiValueFilter.Split(jobs);
        var jobSet = jobFilter == null ? null : new HashSet<string>(jobFilter, StringComparer.OrdinalIgnoreCase);
        var omSet = LeaderSet(om); var ocSet = LeaderSet(oc); var socSet = LeaderSet(soc); var odSet = LeaderSet(od);
        var leadersByMonth = new Dictionary<int, Dictionary<string, Leaders>>();
        foreach (var mon in data.Projected.Select(c => c.Month).Distinct())
            leadersByMonth[mon] = await GetLeadershipAsync(year, mon);
        bool StoreOk(int i, int mon) =>
            (accessibleSet == null || accessibleSet.Contains(data.Stores[i])) && (storeSet == null || storeSet.Contains(data.Stores[i])) &&
            LeadersMatch(leadersByMonth.TryGetValue(mon, out var lm) && lm.TryGetValue(data.Stores[i].Trim(), out var lead) ? lead : null, omSet, ocSet, socSet, odSet);
        bool JobOk(int j) => jobSet == null || jobSet.Contains(data.Jobs[j]);

        var actualMonths = data.Actual.Select(c => c.Month).Distinct().ToHashSet();
        var wanted = months is { Count: > 0 } ? months.ToHashSet() : null;

        // Only stores planned for a month are compared in that month (same rule as the page).
        var planned = data.Projected.Where(c => (wanted == null || wanted.Contains(c.Month)) && StoreOk(c.Store, c.Month))
            .Select(c => (c.Month, c.Store)).ToHashSet();
        var cells = new Dictionary<(int Month, int Store, int Job), int[]>(); // [projected, actual]
        foreach (var c in data.Projected)
        {
            if (!planned.Contains((c.Month, c.Store)) || !JobOk(c.Job)) continue;
            Bump(cells, (c.Month, c.Store, c.Job), 0, c.Count);
        }
        foreach (var c in data.Actual)
        {
            if (!planned.Contains((c.Month, c.Store)) || !JobOk(c.Job)) continue;
            Bump(cells, (c.Month, c.Store, c.Job), 1, c.Count);
        }
        foreach (var kv in cells.OrderBy(k => k.Key.Month).ThenBy(k => data.Stores[k.Key.Store], StringComparer.OrdinalIgnoreCase).ThenBy(k => data.Jobs[k.Key.Job], StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value[0] == 0 && kv.Value[1] == 0) continue;
            rows.Add(new PlanningDetailRow
            {
                Year = year, Month = kv.Key.Month, Store = data.Stores[kv.Key.Store], Job = data.Jobs[kv.Key.Job],
                Projected = kv.Value[0], Actual = actualMonths.Contains(kv.Key.Month) ? kv.Value[1] : null,
            });
            if (leadersByMonth.TryGetValue(kv.Key.Month, out var lm) && lm.TryGetValue(data.Stores[kv.Key.Store].Trim(), out var lead))
            {
                var row = rows[^1];
                row.OperationConsultant = lead.Oc; row.OperationManager = lead.Om;
                row.SeniorOperationConsultant = lead.Soc; row.OperationDirector = lead.Od;
            }
        }
        // Expected resignations and hiring need for months that have a roster.
        foreach (var month in rows.Where(r => r.Actual.HasValue).Select(r => r.Month).Distinct().ToList())
        {
            var attrition = await GetAttritionAsync(year, month);
            foreach (var r in rows.Where(r => r.Month == month && r.Actual.HasValue))
            {
                var expected = attrition.PerMonth.TryGetValue(AttrKey(r.Store, r.Job), out var e) ? e : 0;
                r.ExpectedAttrition = Math.Round(expected, 2);
                r.HiringNeed = Math.Round(Math.Max(0, r.Projected - r.Actual!.Value + expected), 2);
            }
        }
        return rows;
    }

    public async Task<List<PeriodItem>> GetProjectionPeriodsAsync()
    {
        var result = new List<PeriodItem>();
        foreach (var y in await GetYearsAsync())
        {
            var data = await GetYearDataAsync(y);
            foreach (var m in data.Projected.Select(c => c.Month).Distinct().OrderBy(m => m))
                result.Add(new PeriodItem { Year = y, Month = m });
        }
        return result;
    }

    public async Task<List<string>> GetProjectionJobsAsync()
    {
        var jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var y in await GetYearsAsync())
        {
            var data = await GetYearDataAsync(y);
            foreach (var j in data.Projected.Select(c => c.Job).Distinct()) jobs.Add(data.Jobs[j]);
        }
        return jobs.OrderBy(j => j, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int RoundNeed(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static void AddNeed(Dictionary<string, double[]> map, string key, double need, double attrition)
    {
        if (!map.TryGetValue(key, out var arr)) map[key] = arr = new double[2];
        arr[0] += need; arr[1] += attrition;
    }

    private static void Bump<TKey>(Dictionary<TKey, int[]> map, TKey key, int slot, int by) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var arr)) map[key] = arr = new int[2];
        arr[slot] += by;
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
