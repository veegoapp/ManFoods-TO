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

    internal static MemoryCacheEntryOptions CacheOptions() =>
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

    // Trainers are Crew who get an allowance, so the roster has no such job. The projection plans them as
    // two jobs of their own, told apart by payroll group (see the Job Payroll Groups reference list).
    public const string CrewTrainerJob = "Crew Trainer";
    public const string HourlyPaidCrewTrainerJob = "Hourly Paid Crew Trainer";
    public static readonly string[] CrewTrainerJobs = { CrewTrainerJob, HourlyPaidCrewTrainerJob };
    private static readonly Dictionary<string, string> DefaultTrainerGroups = new()
    {
        [CrewTrainerJob] = "Manfoods Company", [HourlyPaidCrewTrainerJob] = "Hourly Paid",
    };

    /// <summary>Which trainer job a payroll group belongs to (normalised group → job), read from the reference
    /// list (normalised job title → group) and falling back to the default groups when the list lacks them.</summary>
    internal static Dictionary<string, string> BuildTrainerJobByGroup(IReadOnlyDictionary<string, string> reference)
    {
        var map = new Dictionary<string, string>();
        foreach (var job in CrewTrainerJobs)
            map[Norm(reference.TryGetValue(Norm(job), out var g) ? g : DefaultTrainerGroups[job])] = job;
        return map;
    }

    internal static string Norm(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ").ToLowerInvariant();
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

    // Each job belongs to one payroll group. Learn it from the active-employee roster of the planned
    // period (else the latest one before it, else the latest overall): the group most of the job's
    // employees are in. Keyed by the normalised job title.
    private async Task<Dictionary<string, string>> GetJobPayrollGroupsAsync(int year, int month)
    {
        var key = $"planning:paygroups:{year}:{month}";
        if (_cache.TryGetValue(key, out Dictionary<string, string>? cached) && cached != null) return cached;

        var map = new Dictionary<string, string>();
        var keys = await GetRosterPeriodKeysAsync();
        if (keys.Count > 0)
        {
            var target = year * 100 + month;
            var period = keys.Where(k => k <= target).DefaultIfEmpty(keys[0]).First();
            var rows = await _db.ActiveEmployees.AsNoTracking()
                .Where(e => e.Year * 100 + e.Month == period && e.PayrollGroup != "")
                .GroupBy(e => new { e.JobTitle, e.PayrollGroup })
                .Select(g => new { g.Key.JobTitle, g.Key.PayrollGroup, Count = g.Count() })
                .ToListAsync();
            foreach (var g in rows.GroupBy(r => Norm(r.JobTitle)))
                map[g.Key] = g.OrderByDescending(r => r.Count).First().PayrollGroup.Trim();
        }

        // The reference list wins over anything learned from the roster.
        foreach (var kv in await GetReferenceGroupsAsync()) map[kv.Key] = kv.Value;
        _cache.Set(key, map, CacheOptions());
        return map;
    }

    // The job → payroll group reference list (a few dozen rows), keyed by the normalised job title.
    private async Task<Dictionary<string, string>> GetReferenceGroupsAsync()
    {
        const string key = "planning:job-group-reference";
        if (_cache.TryGetValue(key, out Dictionary<string, string>? cached) && cached != null) return cached;
        var rows = await _db.JobPayrollGroups.AsNoTracking().Select(j => new { j.JobTitle, j.PayrollGroup }).ToListAsync();
        var map = new Dictionary<string, string>();
        foreach (var r in rows)
            if (!string.IsNullOrWhiteSpace(r.JobTitle) && !string.IsNullOrWhiteSpace(r.PayrollGroup)) map[Norm(r.JobTitle)] = r.PayrollGroup.Trim();
        _cache.Set(key, map, CacheOptions());
        return map;
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

        // Resigned trainers are filed under their roster job (Crew ...) in the Resignations file. Spot them by
        // employee id: anyone on a trainer list in the window (or the month before it) who resigned in the
        // window is a trainer resignation, taken out of the job they were filed under (trainers are planned
        // apart from Crew, so they must not count in both) and put on the trainer job of their payroll group.
        if (used.Count > 0)
        {
            var window = (await GetRosterPeriodKeysAsync()).Where(k => k <= year * 100 + month).Take(AttritionLookbackMonths + 1).ToList();
            var trainerJobByGroup = BuildTrainerJobByGroup(await GetReferenceGroupsAsync());
            var resigned = await (
                from r in _db.Resignations.AsNoTracking()
                where used.Contains(r.Year * 100 + r.Month)
                join t in _db.CrewTrainerEmployees.AsNoTracking() on r.EmployeeId equals t.EmployeeId
                where window.Contains(t.Year * 100 + t.Month)
                select new { r.Store, r.JobTitle, r.EmployeeId, t.PayrollGroup }).Distinct().ToListAsync();
            foreach (var r in resigned.GroupBy(r => r.EmployeeId).Select(g => g.First()))
            {
                if (!trainerJobByGroup.TryGetValue(Norm(r.PayrollGroup), out var trainerJob)) continue;
                var share = 1 / (double)used.Count;
                var from = AttrKey(r.Store, r.JobTitle);
                if (data.PerMonth.TryGetValue(from, out var left)) data.PerMonth[from] = Math.Max(0, left - share);
                var to = AttrKey(r.Store, trainerJob);
                data.PerMonth[to] = (data.PerMonth.TryGetValue(to, out var prev) ? prev : 0) + share;
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
        var counts = new Dictionary<(int Month, string Store, string Job), int>();
        foreach (var a in actual) counts[(a.Month, a.Store, a.JobTitle)] = a.Count;

        // Trainers are Crew who get an allowance, but the projection plans them as jobs of their own on top of
        // Crew. So the people on the monthly allowance list move out of their roster job (and store) into the
        // trainer job of their payroll group (from the list), which keeps the total headcount unchanged.
        // Matched by employee id against that month's roster: only people really on the roster move (a
        // resigned employee or a wrong month never inflates a store), and people whose payroll group is not
        // one of the trainer groups stay where they are. A month without a roster is skipped, so a list alone
        // never makes a month look "actual". The store is the list's, falling back to the roster's.
        var trainerJobByGroup = BuildTrainerJobByGroup(await GetReferenceGroupsAsync());
        var trainers = await (
            from t in _db.CrewTrainerEmployees.AsNoTracking()
            where t.Year == year
            join e in _db.ActiveEmployees.AsNoTracking() on new { t.Year, t.Month, t.EmployeeId } equals new { e.Year, e.Month, e.EmployeeId }
            select new { t.Month, ListStore = t.StoreName, t.PayrollGroup, RosterStore = e.Store, RosterJob = e.JobTitle }).ToListAsync();
        foreach (var t in trainers)
        {
            if (!trainerJobByGroup.TryGetValue(Norm(t.PayrollGroup), out var trainerJob)) continue;
            var from = (t.Month, t.RosterStore, t.RosterJob);
            if (counts.TryGetValue(from, out var n)) counts[from] = Math.Max(0, n - 1);
            var to = (t.Month, string.IsNullOrWhiteSpace(t.ListStore) ? t.RosterStore : t.ListStore, trainerJob);
            counts[to] = (counts.TryGetValue(to, out var m) ? m : 0) + 1;
        }
        var actualCells = counts.Where(c => c.Value > 0).Select(c => new Cell(c.Key.Month, Store(c.Key.Store), Job(c.Key.Job), c.Value)).ToList();
        data.Actual = actualCells.ToArray();

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
        // Default period: the latest month that has an uploaded roster (Monthly Workforce Data), not the
        // calendar month, so the page keeps showing real data until the new month's roster is uploaded.
        var rosterKeys = await GetRosterPeriodKeysAsync();
        var latestRosterYear = rosterKeys.Count > 0 ? rosterKeys[0] / 100 : 0;
        var y = year.HasValue && years.Contains(year.Value) ? year.Value
              : years.Contains(latestRosterYear) ? latestRosterYear
              : (years.Contains(now.Year) ? now.Year : years[0]);
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
        var latestActual = actualMonths.Intersect(projMonths).DefaultIfEmpty(0).Max(); // latest month with both a roster and a projection
        var m = month.HasValue && projMonths.Contains(month.Value) ? month.Value
              : latestActual > 0 ? latestActual
              : (y == now.Year && projMonths.Contains(now.Month)) ? now.Month
              : (projMonths.Count > 0 ? projMonths[^1] : 1);
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
            .OrderBy(r => dto.HasActual ? r.Gap : -r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(); // largest shortage first
        dto.ByJob = sel.ByJob.Select(kv => Row(data.Jobs[kv.Key], kv.Value, dto.HasActual))
            .OrderByDescending(r => r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var r in dto.ByStore) r.OperationConsultant = leaders.TryGetValue(r.Name.Trim(), out var l) ? l.Oc : "";

        var actualTotal = sel.Actual ?? 0;
        dto.Kpis = new PlanningKpiDto
        {
            Projected = sel.Projected,
            Actual = actualTotal,
            Gap = actualTotal - sel.Projected,
            FillPercent = sel.Projected > 0 ? Math.Round(actualTotal * 100.0 / sel.Projected, 1) : 0,
            StoresCount = dto.ByStore.Count,
            StoresShort = dto.HasActual ? dto.ByStore.Count(r => r.Status is "watch" or "critical") : 0,
        };

        // Hiring need: the shortage (people missing in the jobs that are short — a surplus in one job
        // never covers another) plus the expected resignations.
        Dictionary<string, double[]>? storeNeed = null; // store -> [need, expected resignations, shortage], unrounded
        Dictionary<string, double[]>? jobNeed = null;   // job   -> same
        if (includeHiring && dto.HasActual)
        {
            var attrition = await GetAttritionAsync(y, m);
            dto.AttritionMonths = attrition.Periods;
            var byStoreNeed = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // [need, attrition, shortage]
            var byJobNeed = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            double totalNeed = 0, totalAttr = 0, totalShort = 0;
            var trainerNames = CrewTrainerJobs.Select(Norm).ToHashSet();
            var shortages = ShortagePerCell(sel.Cells, k => k.Store, k => trainerNames.Contains(Norm(data.Jobs[k.Job])));
            foreach (var cell in sel.Cells)
            {
                var store = data.Stores[cell.Key.Store]; var job = data.Jobs[cell.Key.Job];
                var expected = attrition.PerMonth.TryGetValue(AttrKey(store, job), out var e) ? e : 0;
                var shortage = shortages[cell.Key];
                var need = shortage + expected;
                AddNeed(byStoreNeed, store, need, expected, shortage); AddNeed(byJobNeed, job, need, expected, shortage);
                totalNeed += need; totalAttr += expected; totalShort += shortage;
            }
            foreach (var r in dto.ByStore) if (byStoreNeed.TryGetValue(r.Name, out var v)) SetNeed(r, v[2], v[1]);
            foreach (var r in dto.ByJob) if (byJobNeed.TryGetValue(r.Name, out var v)) SetNeed(r, v[2], v[1]);
            dto.Kpis.Shortage = RoundNeed(totalShort);
            dto.Kpis.ExpectedAttrition = Math.Round(totalAttr, 1);
            dto.Kpis.HiringNeed = dto.Kpis.Shortage + RoundNeed(dto.Kpis.ExpectedAttrition);
            storeNeed = byStoreNeed;
            jobNeed = byJobNeed;
        }

        // Payroll groups: sum the per-job figures into each job's payroll group.
        if (includeHiring)
        {
            var groupOfJob = await GetJobPayrollGroupsAsync(y, m);
            var accPg = new Dictionary<string, (int P, int A, double Need, double Attr, double Short)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sel.ByJob)
            {
                var job = data.Jobs[kv.Key];
                var group = groupOfJob.TryGetValue(Norm(job), out var g) ? g : "";
                double need = 0, attr = 0, shortage = 0;
                if (jobNeed != null && jobNeed.TryGetValue(job, out var jn)) { need = jn[0]; attr = jn[1]; shortage = jn[2]; }
                accPg.TryGetValue(group, out var a);
                accPg[group] = (a.P + kv.Value[0], a.A + (dto.HasActual ? kv.Value[1] : 0), a.Need + need, a.Attr + attr, a.Short + shortage);
            }
            dto.ByPayrollGroup = accPg.Select(kv =>
                {
                    var row = Row(kv.Key, new[] { kv.Value.P, kv.Value.A }, dto.HasActual);
                    SetNeed(row, kv.Value.Short, kv.Value.Attr);
                    return row;
                })
                .OrderBy(r => dto.HasActual ? r.Gap : -r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Roll the compared stores up by the people responsible for them. Figures are summed from
        // the stores (hiring need from the unrounded per-store values), so each table adds up.
        if (includeHiring)
        {
            List<PlanningRowDto> RollUp(Func<Leaders, string> pick)
            {
                var acc = new Dictionary<string, (int Stores, int P, int A, double Need, double Attr, double Short)>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in dto.ByStore)
                {
                    if (!leaders.TryGetValue(r.Name.Trim(), out var l)) continue;
                    var name = pick(l);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    acc.TryGetValue(name, out var a);
                    double need = 0, attr = 0, shortage = 0;
                    if (storeNeed != null && storeNeed.TryGetValue(r.Name, out var sn)) { need = sn[0]; attr = sn[1]; shortage = sn[2]; }
                    acc[name] = (a.Stores + 1, a.P + r.Projected, a.A + (dto.HasActual ? r.Actual : 0), a.Need + need, a.Attr + attr, a.Short + shortage);
                }
                return acc.Select(kv =>
                    {
                        var row = Row(kv.Key, new[] { kv.Value.P, kv.Value.A }, dto.HasActual);
                        row.StoreCount = kv.Value.Stores;
                        SetNeed(row, kv.Value.Short, kv.Value.Attr);
                        return row;
                    })
                    .OrderBy(r => dto.HasActual ? r.Gap : -r.Projected).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
        var trainerNames = CrewTrainerJobs.Select(Norm).ToHashSet();
        var detailShortage = ShortagePerCell(cells, k => (k.Month, k.Store), k => trainerNames.Contains(Norm(data.Jobs[k.Job])));
        foreach (var kv in cells.OrderBy(k => k.Key.Month).ThenBy(k => data.Stores[k.Key.Store], StringComparer.OrdinalIgnoreCase).ThenBy(k => data.Jobs[k.Key.Job], StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value[0] == 0 && kv.Value[1] == 0) continue;
            rows.Add(new PlanningDetailRow
            {
                Year = year, Month = kv.Key.Month, Store = data.Stores[kv.Key.Store], Job = data.Jobs[kv.Key.Job],
                Projected = kv.Value[0], Actual = actualMonths.Contains(kv.Key.Month) ? kv.Value[1] : null,
                Shortage = actualMonths.Contains(kv.Key.Month) ? Math.Round(detailShortage[kv.Key], 2) : null,
            });
            if (leadersByMonth.TryGetValue(kv.Key.Month, out var lm) && lm.TryGetValue(data.Stores[kv.Key.Store].Trim(), out var lead))
            {
                var row = rows[^1];
                row.OperationConsultant = lead.Oc; row.OperationManager = lead.Om;
                row.SeniorOperationConsultant = lead.Soc; row.OperationDirector = lead.Od;
            }
        }
        // Payroll group of each row's job (same month-aware lookup the page uses).
        foreach (var month in rows.Select(r => r.Month).Distinct().ToList())
        {
            var groups = await GetJobPayrollGroupsAsync(year, month);
            foreach (var r in rows.Where(r => r.Month == month))
                r.PayrollGroup = groups.TryGetValue(Norm(r.Job), out var pg) ? pg : "";
        }

        // Expected resignations and hiring need for months that have a roster.
        foreach (var month in rows.Where(r => r.Actual.HasValue).Select(r => r.Month).Distinct().ToList())
        {
            var attrition = await GetAttritionAsync(year, month);
            foreach (var r in rows.Where(r => r.Month == month && r.Actual.HasValue))
            {
                var expected = attrition.PerMonth.TryGetValue(AttrKey(r.Store, r.Job), out var e) ? e : 0;
                r.ExpectedAttrition = Math.Round(expected, 2);
                r.HiringNeed = Math.Round((r.Shortage ?? 0) + expected, 2); // shortage + expected resignations
            }
        }
        return rows;
    }

    // Headcount per store+job in one roster period (only used when that period is in an earlier year
    // than the one being forecast, so the year's own data can't supply the starting point).
    private async Task<Dictionary<string, int>> GetPeriodHeadcountAsync(int period)
    {
        var key = $"planning:baseline:{period}";
        if (_cache.TryGetValue(key, out Dictionary<string, int>? cached) && cached != null) return cached;
        var rows = await _db.ActiveEmployees.AsNoTracking()
            .Where(e => e.Year * 100 + e.Month == period)
            .GroupBy(e => new { e.Store, e.JobTitle })
            .Select(g => new { g.Key.Store, g.Key.JobTitle, Count = g.Count() })
            .ToListAsync();
        var map = new Dictionary<string, int>();
        foreach (var r in rows) { var k = AttrKey(r.Store, r.JobTitle); map[k] = (map.TryGetValue(k, out var v) ? v : 0) + r.Count; }
        _cache.Set(key, map, CacheOptions());
        return map;
    }

    public async Task<HiringForecastDto> GetHiringForecastAsync(int? year, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, double earlyLeaverPercent = 0, string? by = null)
    {
        var dto = new HiringForecastDto();
        var years = await GetYearsAsync();
        dto.Years = years;
        if (years.Count == 0) return dto;
        dto.HasData = true;

        var now = DateTime.Now;
        var y = year.HasValue && years.Contains(year.Value) ? year.Value : (years.Contains(now.Year) ? now.Year : years[0]);
        dto.Year = y;
        var data = await GetYearDataAsync(y);

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var accessibleSet = accessible == null ? null : new HashSet<string>(accessible.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        var storeSet = LeaderSet(stores);
        var jobSet = LeaderSet(jobs);
        var omSet = LeaderSet(om); var ocSet = LeaderSet(oc); var socSet = LeaderSet(soc); var odSet = LeaderSet(od);
        var leaders = await GetLeadershipAsync(y, 12);

        bool StoreOk(int i) =>
            (accessibleSet == null || accessibleSet.Contains(data.Stores[i])) &&
            (storeSet == null || storeSet.Contains(data.Stores[i])) &&
            LeadersMatch(leaders.TryGetValue(data.Stores[i].Trim(), out var lead) ? lead : null, omSet, ocSet, socSet, odSet);

        var visible = data.Projected.Select(c => c.Store).Distinct().Where(i => accessibleSet == null || accessibleSet.Contains(data.Stores[i])).ToList();
        dto.Stores = visible.Select(i => data.Stores[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        dto.Jobs = data.Projected.Select(c => c.Job).Distinct().Select(i => data.Jobs[i]).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var visibleLeaders = visible.Select(i => leaders.TryGetValue(data.Stores[i].Trim(), out var vl) ? vl : null).Where(vl => vl != null).Select(vl => vl!).ToList();
        List<string> Options(Func<Leaders, string> pick) => visibleLeaders.Select(pick).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        dto.OperationConsultants = Options(l => l.Oc); dto.OperationManagers = Options(l => l.Om);
        dto.SeniorOperationConsultants = Options(l => l.Soc); dto.OperationDirectors = Options(l => l.Od);

        // Where the forecast starts: the latest uploaded roster.
        var rosterKeys = await GetRosterPeriodKeysAsync();
        var baseline = rosterKeys.Count > 0 ? rosterKeys[0] : 0;
        dto.HasRoster = baseline > 0;
        dto.BaselineYear = baseline / 100; dto.BaselineMonth = baseline % 100;
        if (!dto.HasRoster) return dto;

        var rate = Math.Clamp(earlyLeaverPercent / 100.0, 0, 0.5);
        dto.EarlyLeaverRate = Math.Round(rate * 100, 1);
        var gross = 1.0 / (1.0 - rate);

        var baseAttr = await GetAttritionAsync(baseline / 100, baseline % 100);
        dto.AttritionMonths = baseAttr.Periods;
        var actualMonths = data.Actual.Select(c => c.Month).Distinct().ToHashSet();
        var projMonths = data.Projected.Select(c => c.Month).Distinct().ToHashSet();
        var attrByMonth = new Dictionary<int, AttritionData>();
        foreach (var mon in actualMonths) attrByMonth[mon] = await GetAttritionAsync(y, mon);

        // The two trainer jobs are forecast together (the store's trainers are what matters, not whether each is
        // hourly paid), so they share one cell: the first trainer job found stands for both.
        var trainerNames = CrewTrainerJobs.Select(Norm).ToHashSet();
        int canonTrainer = -1;
        int Canon(int job)
        {
            if (!trainerNames.Contains(Norm(data.Jobs[job]))) return job;
            if (canonTrainer < 0) canonTrainer = job;
            return canonTrainer;
        }
        var actual = new Dictionary<(int Month, int Store, int Job), int>();
        foreach (var c in data.Actual) { var key = (c.Month, c.Store, Canon(c.Job)); actual[key] = (actual.TryGetValue(key, out var v) ? v : 0) + c.Count; }
        var projected = new Dictionary<(int Store, int Job), int[]>();
        foreach (var c in data.Projected)
        {
            if (!StoreOk(c.Store) || (jobSet != null && !jobSet.Contains(data.Jobs[c.Job]))) continue;
            var key = (c.Store, Canon(c.Job));
            if (!projected.TryGetValue(key, out var arr)) projected[key] = arr = new int[13];
            arr[c.Month] += c.Count;
        }
        // Expected resignations / starting headcount of a cell: both trainer jobs added up for the shared trainer cell.
        double Sum(int store, int job, Func<string, double> lookup) =>
            job == canonTrainer ? CrewTrainerJobs.Sum(j => lookup(AttrKey(data.Stores[store], j))) : lookup(AttrKey(data.Stores[store], data.Jobs[job]));
        // The starting headcount of a forecast that begins before the year's own data: the roster
        // of an earlier year (when the baseline is later than this year, nothing is simulated).
        Dictionary<string, int>? earlier = baseline / 100 < y ? await GetPeriodHeadcountAsync(baseline) : null;

        // Row dimension: store (default), job, payroll group or operation consultant.
        var mode = (by ?? "store").Trim().ToLowerInvariant();
        dto.By = mode is "job" or "payroll" or "consultant" ? mode : "store";
        var groupOfJob = dto.By == "payroll" ? await GetJobPayrollGroupsAsync(y, 12) : null;
        string RowKey(int store, int job) => dto.By switch
        {
            "job" => job == canonTrainer ? CrewTrainerJob + " + " + HourlyPaidCrewTrainerJob : data.Jobs[job],
            "payroll" => groupOfJob!.TryGetValue(Norm(data.Jobs[job]), out var g) ? g : "",
            "consultant" => leaders.TryGetValue(data.Stores[store].Trim(), out var lc) ? lc.Oc : "",
            _ => data.Stores[store],
        };
        var byStore = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var storeMonths = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // always per store, for the leader roll-ups
        foreach (var kv in projected)
        {
            var (store, job) = kv.Key;
            var rowKey = RowKey(store, job);
            var storeAcc = storeMonths.TryGetValue(data.Stores[store].Trim(), out var sm) ? sm : (storeMonths[data.Stores[store].Trim()] = new double[12]);
            var acc = byStore.TryGetValue(rowKey, out var existing) ? existing : (byStore[rowKey] = new double[12]);
            double? prev = null;
            for (int mon = 1; mon <= 12; mon++)
            {
                var p = kv.Value[mon];
                if (!projMonths.Contains(mon)) continue;
                double hires;
                if (actualMonths.Contains(mon))
                {
                    var a = actual.TryGetValue((mon, store, job), out var av) ? av : 0;
                    var exp = Sum(store, job, k => attrByMonth[mon].PerMonth.TryGetValue(k, out var e1) ? e1 : 0);
                    hires = Math.Max(0, p - a) + exp; // shortage + expected resignations, as on Workforce Planning
                    prev = a;
                }
                else
                {
                    if (prev == null)
                    {
                        if (earlier == null) continue; // no starting point in or before this year
                        prev = Sum(store, job, k => earlier.TryGetValue(k, out var b) ? b : 0);
                    }
                    var exp = Sum(store, job, k => baseAttr.PerMonth.TryGetValue(k, out var e2) ? e2 : 0);
                    var before = Math.Max(0, prev.Value - exp);
                    var net = Math.Max(0, p - before);
                    hires = net;
                    prev = before + net;
                }
                acc[mon - 1] += hires * gross;
                storeAcc[mon - 1] += hires * gross;
            }
        }

        // Round each store-month once, then add the rounded cells, so every row and column adds up exactly.
        foreach (var kv in byStore.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = new HiringForecastRowDto
            {
                Store = kv.Key,
                OperationConsultant = dto.By == "store" && leaders.TryGetValue(kv.Key.Trim(), out var l) ? l.Oc : "",
            };
            for (int i = 0; i < 12; i++) { row.Months[i] = RoundNeed(kv.Value[i]); row.Total += row.Months[i]; dto.MonthTotals[i] += row.Months[i]; }
            dto.GrandTotal += row.Total;
            dto.Rows.Add(row);
        }

        // Per-leader roll-ups: the same rounded store-month cells added up under each store's leader.
        List<HiringForecastRowDto> RollUpLeaders(Func<Leaders, string> pick)
        {
            var groups = new Dictionary<string, HiringForecastRowDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in storeMonths)
            {
                if (!leaders.TryGetValue(kv.Key, out var lead)) continue;
                var name = (pick(lead) ?? "").Trim();
                if (name.Length == 0) continue;
                if (!groups.TryGetValue(name, out var g)) groups[name] = g = new HiringForecastRowDto { Store = name };
                g.StoreCount++;
                for (int i = 0; i < 12; i++) { var n = RoundNeed(kv.Value[i]); g.Months[i] += n; g.Total += n; }
            }
            return groups.Values.OrderByDescending(g => g.Total).ThenBy(g => g.Store, StringComparer.OrdinalIgnoreCase).ToList();
        }
        dto.ByOperationConsultant = RollUpLeaders(l => l.Oc);
        dto.BySeniorOperationConsultant = RollUpLeaders(l => l.Soc);
        dto.ByOperationDirector = RollUpLeaders(l => l.Od);
        dto.ByOperationManager = RollUpLeaders(l => l.Om);
        for (int mon = 1; mon <= 12; mon++)
            dto.MonthModes[mon - 1] = !projMonths.Contains(mon) ? "none" : actualMonths.Contains(mon) ? "actual" : (baseline / 100 < y || actualMonths.Count > 0) ? "forecast" : "none";
        return dto;
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

    // Shortage, expected resignations and hiring need of a row. Hiring need is always the shortage plus the
    // expected resignations as displayed (rounded), so the three columns add up on screen.
    private static void SetNeed(PlanningRowDto row, double shortage, double attrition)
    {
        row.Shortage = RoundNeed(shortage);
        row.ExpectedAttrition = Math.Round(attrition, 1);
        row.HiringNeed = row.Shortage + RoundNeed(row.ExpectedAttrition);
    }

    // Shortage per store+job cell. The two trainer jobs are counted together (the store's trainers are what
    // matters, not whether each is hourly paid): a surplus in one covers a shortage in the other.
    private static Dictionary<TKey, double> ShortagePerCell<TKey>(IEnumerable<KeyValuePair<TKey, int[]>> cells, Func<TKey, object> storeOf, Func<TKey, bool> isTrainerJob) where TKey : notnull
    {
        var result = new Dictionary<TKey, double>();
        foreach (var kv in cells) result[kv.Key] = Math.Max(0, kv.Value[0] - kv.Value[1]);
        foreach (var group in cells.Where(c => isTrainerJob(c.Key)).GroupBy(c => storeOf(c.Key)))
        {
            var list = group.ToList();
            if (list.Count < 2) continue;
            double positive = list.Sum(c => Math.Max(0, c.Value[0] - c.Value[1]));
            double surplus = list.Sum(c => Math.Max(0, c.Value[1] - c.Value[0]));
            double net = Math.Max(0, positive - surplus);
            foreach (var c in list)
                result[c.Key] = positive > 0 ? Math.Max(0, c.Value[0] - c.Value[1]) * net / positive : 0;
        }
        return result;
    }

    private static int RoundNeed(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static void AddNeed(Dictionary<string, double[]> map, string key, double need, double attrition, double shortage)
    {
        if (!map.TryGetValue(key, out var arr)) map[key] = arr = new double[3];
        arr[0] += need; arr[1] += attrition; arr[2] += shortage;
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
            Name = name, Projected = p, Actual = a, Gap = a - p, FillPercent = fill,
            Status = !hasActual ? "none" : p == 0 ? "ok" : a > p ? "over" : fill >= OkFill ? "ok" : fill >= WatchFill ? "watch" : "critical",
        };
    }
}
