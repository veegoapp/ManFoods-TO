using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ScorecardService : IScorecardService
{
    // Shared with UploadService, which invalidates this key whenever it writes
    // to ActiveEmployees or Resignations (upload, single-file update, delete) —
    // so a fresh Excel import is reflected immediately with no stale window.
    public const string HistoricalRecordsCacheKey = "scorecard:historical-records";

    private readonly AppDbContext _db;
    private readonly IExitInterviewService _exitInterviews;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContext;

    public ScorecardService(AppDbContext db, IExitInterviewService exitInterviews, IStoreAccessService storeAccess, IMemoryCache cache, IHttpContextAccessor httpContext)
    {
        _db = db;
        _exitInterviews = exitInterviews;
        _storeAccess = storeAccess;
        _cache = cache;
        _httpContext = httpContext;
    }

    private List<string>? RequestedJobs =>
        MultiValueFilter.Split(_httpContext.HttpContext?.Request.Query["jobs"].ToString());

    private IQueryable<ActiveEmployee> ApplyActiveJobFilter(IQueryable<ActiveEmployee> query) =>
        RequestedJobs is { } jobs ? query.Where(e => jobs.Contains(e.JobTitle)) : query;

    private IQueryable<Resignation> ApplyResignationJobFilter(IQueryable<Resignation> query) =>
        RequestedJobs is { } jobs ? query.Where(e => jobs.Contains(e.JobTitle)) : query;

    private static string Pick(StoreReference s, string dimension) => dimension switch
    {
        "leader" => s.StoreLeader,
        "oc" => s.OperationConsultant,
        "om" => s.OperationManager,
        "soc" => s.SeniorOperationConsultant,
        "od" => s.OperationDirector,
        _ => "",
    };

    private static string NormalizeProfileDimension(string dimension) =>
        dimension is "leader" or "oc" or "om" ? dimension : "leader";

    private static IQueryable<StoreReference> WhereDimension(
        IQueryable<StoreReference> query, string dimension, string name) => dimension switch
        {
            "oc" => query.Where(s => s.OperationConsultant == name),
            "om" => query.Where(s => s.OperationManager == name),
            _ => query.Where(s => s.StoreLeader == name),
        };

    private class NameAggregate
    {
        public HashSet<string> Stores { get; } = new();
        public Dictionary<(int Month, int Year), int> PeriodHeadcount { get; } = new();
        public int TotalResignations;
        public string LatestOc = "";
        public string LatestOm = "";
        public string LatestSoc = "";
        public string LatestOd = "";
    }

    /// <summary>
    /// When no year is supplied by the caller, returns the latest year present
    /// in store_reference so the scorecard is never blank due to DateTime.Now
    /// drifting ahead of the data. Returns null only when the table is empty.
    /// </summary>
    private async Task<int?> ResolveEffectiveYearAsync(int? year)
    {
        if (year.HasValue) return year;
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Year })
            .FirstOrDefaultAsync();
        return latest?.Year;
    }

    // Walks every StoreReference row in the resolved window for the given dimension,
    // following each person across whichever store(s) they were assigned to in each
    // period — instead of only looking at the single latest period's assignment.
    private async Task<Dictionary<string, NameAggregate>> BuildNameAggregatesAsync(
        string dimension, string role, string? assignedName, string? om, string? oc, string? soc, string? od, string? months, int? year)
    {
        var periods = DashboardService.ResolvePeriods(null, year, null, null, months);
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        if (periodKeys.Count == 0) return new Dictionary<string, NameAggregate>();

        var storeRefs = await _db.StoreReferences.Where(s => periodKeys.Contains(s.Year * 100 + s.Month)).ToListAsync();
        // Role-based store access is always applied first — never bypassed by
        // the om/oc filter, which only narrows further on top of it.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) storeRefs = storeRefs.Where(s => accessible.Contains(s.StoreName)).ToList();
        if (MultiValueFilter.Split(om) is { } oms) storeRefs = storeRefs.Where(s => oms.Contains(s.OperationManager)).ToList();
        if (MultiValueFilter.Split(oc) is { } ocs) storeRefs = storeRefs.Where(s => ocs.Contains(s.OperationConsultant)).ToList();
        if (MultiValueFilter.Split(soc) is { } socs) storeRefs = storeRefs.Where(s => socs.Contains(s.SeniorOperationConsultant)).ToList();
        if (MultiValueFilter.Split(od) is { } ods) storeRefs = storeRefs.Where(s => ods.Contains(s.OperationDirector)).ToList();

        var headcountMap = (await ApplyActiveJobFilter(_db.ActiveEmployees.Where(e => periodKeys.Contains(e.Year * 100 + e.Month)))
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        var resignMap = (await ApplyResignationJobFilter(_db.Resignations.Where(r => periodKeys.Contains(r.Year * 100 + r.Month)))
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        var result = new Dictionary<string, NameAggregate>();
        foreach (var sr in storeRefs.OrderBy(s => s.Year).ThenBy(s => s.Month))
        {
            var name = Pick(sr, dimension);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sr.StoreName)) continue;
            if (!result.TryGetValue(name, out var agg)) { agg = new NameAggregate(); result[name] = agg; }

            var period = (sr.Month, sr.Year);
            var hc = headcountMap.GetValueOrDefault((sr.StoreName, sr.Month, sr.Year));
            var res = resignMap.GetValueOrDefault((sr.StoreName, sr.Month, sr.Year));

            agg.Stores.Add(sr.StoreName);
            agg.PeriodHeadcount[period] = agg.PeriodHeadcount.GetValueOrDefault(period) + hc;
            agg.TotalResignations += res;
            agg.LatestOc = sr.OperationConsultant;
            agg.LatestOm = sr.OperationManager;
            agg.LatestSoc = sr.SeniorOperationConsultant;
            agg.LatestOd = sr.OperationDirector;
        }
        return result;
    }

    private class HistoricalRecord
    {
        public string Store { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public int? TenureDays { get; set; }
        public DateOnly HireDate { get; set; }
        /// <summary>YYYYMM key derived from the employee's hire date — used to scope
        /// early-leaver / retention rates to the selected period.</summary>
        public int HireDateKey { get; set; }
    }

    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
        // This query has no parameters — it returns the same rows regardless of
        // dimension/year/months (those are applied afterward by the caller) — so
        // it's cached whole rather than re-read from the database on every Scorecard
        // interaction (dimension click, year/month change). See UploadService for
        // the write-side invalidation of HistoricalRecordsCacheKey.
        if (_cache.TryGetValue(HistoricalRecordsCacheKey, out List<HistoricalRecord>? cached) && cached != null)
            return RequestedJobs is { } cachedJobs ? cached.Where(r => cachedJobs.Contains(r.JobTitle)).ToList() : cached;

        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.JobTitle, e.HireDate })
            .ToListAsync();
        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.JobTitle, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, HistoricalRecord>();
        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new HistoricalRecord
            {
                Store       = a.Store,
                JobTitle    = a.JobTitle,
                TenureDays  = null,
                HireDate    = a.HireDate!.Value,
                HireDateKey = a.HireDate!.Value.Year * 100 + a.HireDate!.Value.Month,
            };
        }
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store       = r.Store,
                JobTitle    = r.JobTitle,
                TenureDays  = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
                HireDate    = r.HireDate!.Value,
                HireDateKey = r.HireDate!.Value.Year * 100 + r.HireDate!.Value.Month,
            };
        }
        var records = byEmployee.Values.ToList();
        _cache.Set(HistoricalRecordsCacheKey, records, TimeSpan.FromHours(6));
        return RequestedJobs is { } jobs ? records.Where(r => jobs.Contains(r.JobTitle)).ToList() : records;
    }

    public async Task<List<ScorecardRow>> GetScorecardAsync(string dimension, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        // Resolve once so both aggregates and the historical period window use
        // the same effective year (latest data year when caller passes no year).
        var effectiveYear = await ResolveEffectiveYearAsync(year);

        var aggregates = await BuildNameAggregatesAsync(dimension, role, assignedName, om, oc, soc, od, months, effectiveYear);
        if (aggregates.Count == 0) return new List<ScorecardRow>();

        var historical = await LoadHistoricalRecordsAsync();

        // Scope early-leaver / retention rates to the selected period (hires whose hire
        // date falls within the resolved window). Falls back to all-time when no period
        // is specified so the scorecard is always populated.
        var periodKeys = DashboardService.ResolvePeriods(null, effectiveYear, null, null, months)
            .Select(p => p.Year * 100 + p.Month)
            .ToHashSet();
        var periodFiltered = periodKeys.Count > 0
            ? historical.Where(h => periodKeys.Contains(h.HireDateKey)).ToList()
            : historical;

        // One batched query for every name's sentiment instead of one query per
        // name (was an N+1 — a separate ExitInterviews round trip per leader/OC/OM).
        var sentimentByName = await _exitInterviews.GetSentimentSummariesByDimensionAsync(
            dimension, aggregates.Keys.ToList(), role, assignedName);

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<ScorecardRow>();
        // Sequential — EF Core DbContext does not support concurrent queries.
        foreach (var (name, agg) in aggregates)
        {
            var avgHeadcount = agg.PeriodHeadcount.Count > 0 ? agg.PeriodHeadcount.Values.Average() : 0;
            var turnoverRate = MetricsCalculationService.RatePercent(agg.TotalResignations, avgHeadcount);

            var records = periodFiltered.Where(h => agg.Stores.Contains(h.Store)).ToList();

            // 90-Day Early Leavers: (resigned with tenure ≤ 90 days) ÷ (total New Hires),
            // gated to the go-live cohort — the same formula and population as
            // NinetyDayTurnoverService and EarlyWarningService.
            var eligibleFor90 = records.Where(r => MetricsCalculationService.IsPastGoLive(r.HireDate)).ToList();
            var early90 = MetricsCalculationService.NinetyDayRate(
                eligibleFor90.Count, eligibleFor90.Count(r => MetricsCalculationService.IsEarlyLeaver(r.TenureDays)));

            // 6-Month Retention: exclude still-active hires too recent to have reached
            // the 180-day milestone — they have no determined outcome yet.
            var eligibleForRetention = records
                .Where(r => MetricsCalculationService.IsEligibleForMilestone(r.HireDate, r.TenureDays, MetricsCalculationService.SixMonthRetentionDays, asOf))
                .ToList();
            var retained180 = MetricsCalculationService.RatePercent(
                eligibleForRetention.Count(r => MetricsCalculationService.IsRetainedAtMilestone(r.TenureDays, MetricsCalculationService.SixMonthRetentionDays)),
                eligibleForRetention.Count);

            var sentiment = sentimentByName[name];

            result.Add(new ScorecardRow
            {
                Name = name,
                StoreCount = agg.Stores.Count,
                Headcount = (int)Math.Round(avgHeadcount),
                TurnoverRate = turnoverRate,
                EarlyLeaver90Rate = early90,
                Retention180Rate = retained180,
                ExitSentimentPercent = sentiment.PositivePercent,
                ExitResponseCount = sentiment.TotalResponses,
            });
        }

        return result.OrderByDescending(r => r.TurnoverRate).ToList();
    }

    public async Task<List<string>> GetLeaderNamesAsync(string role, string? assignedName)
    {
        var q = _db.StoreReferences.Where(s => s.StoreLeader != "");
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Select(s => s.StoreLeader).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<StoreLeaderProfileViewModel> GetLeaderProfileAsync(string leaderName, string role, string? assignedName, string? months = null, int? year = null, string dimension = "leader")
    {
        dimension = NormalizeProfileDimension(dimension);
        var profile = new StoreLeaderProfileViewModel { Name = leaderName };
        if (string.IsNullOrWhiteSpace(leaderName)) return profile;

        var effectiveYear = await ResolveEffectiveYearAsync(year);
        var scorecard = await GetScorecardAsync(dimension, role, assignedName, months: months, year: effectiveYear);
        profile.Summary = scorecard.FirstOrDefault(r => r.Name == leaderName);
        profile.History = await GetLeaderHistoryAsync(leaderName, role, assignedName, months, effectiveYear, dimension);

        var filter = new ExitInterviewFilter
        {
            Months = months,
            Year = effectiveYear,
        };
        if (dimension == "leader") filter.StoreLeader = leaderName;
        else if (dimension == "oc") filter.OperationConsultant = leaderName;
        else filter.OperationManager = leaderName;

        profile.ExitSentiment = await _exitInterviews.GetSentimentSummaryAsync(filter, role, assignedName);
        profile.ExitReasons = await _exitInterviews.GetReasonsForLeavingAsync(filter, role, assignedName);
        profile.WouldReturn = await _exitInterviews.GetWouldReturnAsync(filter, role, assignedName);
        profile.OverallExperience = await _exitInterviews.GetOverallExperienceAsync(filter, role, assignedName);
        profile.WorkloadCondition = await _exitInterviews.GetWorkloadConditionAsync(filter, role, assignedName);
        profile.Training = await _exitInterviews.GetTrainingAsync(filter, role, assignedName);
        profile.FairTreatment = await _exitInterviews.GetFairTreatmentAsync(filter, role, assignedName);
        profile.JobTitles = await _exitInterviews.GetByJobTitleAsync(filter, role, assignedName);
        profile.EngagementDrivers = await _exitInterviews.GetEngagementDriversAsync(filter, role, assignedName);
        profile.Comments = await _exitInterviews.GetCommentsAsync(filter, role, assignedName);
        profile.ReasonsTrend = await _exitInterviews.GetReasonsTrendAsync(filter, role, assignedName);
        profile.ReasonVsWouldReturn = await _exitInterviews.GetReasonVsWouldReturnAsync(filter, role, assignedName);
        return profile;
    }

    public async Task<List<LeaderHistoryRow>> GetLeaderHistoryAsync(string leaderName, string role, string? assignedName, string? months = null, int? year = null, string dimension = "leader")
    {
        if (string.IsNullOrWhiteSpace(leaderName)) return new List<LeaderHistoryRow>();
        dimension = NormalizeProfileDimension(dimension);

        var periods = DashboardService.ResolvePeriods(null, year, null, null, months);
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        if (periodKeys.Count == 0) return new List<LeaderHistoryRow>();

        var rowsQuery = WhereDimension(
            _db.StoreReferences.Where(s => periodKeys.Contains(s.Year * 100 + s.Month)),
            dimension, leaderName);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) rowsQuery = rowsQuery.Where(s => accessible.Contains(s.StoreName));
        var rows = await rowsQuery
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync();
        if (rows.Count == 0) return new List<LeaderHistoryRow>();

        var headcountMap = (await ApplyActiveJobFilter(_db.ActiveEmployees.Where(e => periodKeys.Contains(e.Year * 100 + e.Month)))
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        var resignMap = (await ApplyResignationJobFilter(_db.Resignations.Where(r => periodKeys.Contains(r.Year * 100 + r.Month)))
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        if (dimension != "leader")
        {
            return rows
                .GroupBy(r => new { r.Year, r.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var stores = g.Select(r => r.StoreName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    var headcount = stores.Sum(store => headcountMap.GetValueOrDefault((store, g.Key.Month, g.Key.Year)));
                    var resignations = stores.Sum(store => resignMap.GetValueOrDefault((store, g.Key.Month, g.Key.Year)));
                    return new LeaderHistoryRow
                    {
                        Store = string.Join(", ", stores),
                        Month = g.Key.Month,
                        Year = g.Key.Year,
                        PeriodLabel = $"{g.Key.Month:00}/{g.Key.Year}",
                        Headcount = headcount,
                        Resignations = resignations,
                        TurnoverRate = MetricsCalculationService.RatePercent(resignations, headcount),
                        IsStoreTransition = false,
                    };
                })
                .ToList();
        }

        var result = new List<LeaderHistoryRow>();
        string? previousStore = null;
        foreach (var r in rows)
        {
            var hc = headcountMap.GetValueOrDefault((r.StoreName, r.Month, r.Year));
            var res = resignMap.GetValueOrDefault((r.StoreName, r.Month, r.Year));
            result.Add(new LeaderHistoryRow
            {
                Store = r.StoreName,
                Month = r.Month,
                Year = r.Year,
                PeriodLabel = $"{r.Month:00}/{r.Year}",
                Headcount = hc,
                Resignations = res,
                TurnoverRate = MetricsCalculationService.RatePercent(res, hc),
                IsStoreTransition = previousStore != null && previousStore != r.StoreName,
            });
            previousStore = r.StoreName;
        }
        return result;
    }

    public async Task<ScorecardRollupResult> GetRollupAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        var result = new ScorecardRollupResult();
        var leaderAggregates = await BuildNameAggregatesAsync("leader", role, assignedName, om, oc, soc, od, months, year);
        if (leaderAggregates.Count == 0) return result;

        var leaderRates = leaderAggregates.Select(kv =>
        {
            var avgHeadcount = kv.Value.PeriodHeadcount.Count > 0 ? kv.Value.PeriodHeadcount.Values.Average() : 0;
            var rate = avgHeadcount > 0 ? kv.Value.TotalResignations * 100.0 / avgHeadcount : 0;
            return (Name: kv.Key, Rate: rate, Oc: kv.Value.LatestOc, Om: kv.Value.LatestOm, Soc: kv.Value.LatestSoc, Od: kv.Value.LatestOd);
        }).ToList();

        var average = leaderRates.Average(l => l.Rate);
        result.AverageTurnoverRate = Math.Round(average, 1);

        result.ByOperationConsultant = leaderRates
            .Where(l => !string.IsNullOrWhiteSpace(l.Oc))
            .GroupBy(l => l.Oc)
            .Select(g => BuildRollupRow(g.Key, g.ToList(), average))
            .OrderByDescending(r => r.FlaggedPercent)
            .ToList();

        result.ByOperationManager = leaderRates
            .Where(l => !string.IsNullOrWhiteSpace(l.Om))
            .GroupBy(l => l.Om)
            .Select(g => BuildRollupRow(g.Key, g.ToList(), average))
            .OrderByDescending(r => r.FlaggedPercent)
            .ToList();

        result.BySeniorOperationConsultant = leaderRates
            .Where(l => !string.IsNullOrWhiteSpace(l.Soc))
            .GroupBy(l => l.Soc)
            .Select(g => BuildRollupRow(g.Key, g.ToList(), average))
            .OrderByDescending(r => r.FlaggedPercent)
            .ToList();

        result.ByOperationDirector = leaderRates
            .Where(l => !string.IsNullOrWhiteSpace(l.Od))
            .GroupBy(l => l.Od)
            .Select(g => BuildRollupRow(g.Key, g.ToList(), average))
            .OrderByDescending(r => r.FlaggedPercent)
            .ToList();

        return result;
    }

    private static RollupRow BuildRollupRow(string name, List<(string Name, double Rate, string Oc, string Om, string Soc, string Od)> leaders, double average)
    {
        var flagged = leaders.Count(l => l.Rate > average);
        return new RollupRow
        {
            Name = name,
            TotalLeaders = leaders.Count,
            FlaggedLeaders = flagged,
            FlaggedPercent = MetricsCalculationService.RatePercent(flagged, leaders.Count),
        };
    }
}
