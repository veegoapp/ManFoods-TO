using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class EarlyWarningService : IEarlyWarningService
{
    // Shared with UploadService, which invalidates these keys whenever it writes
    // to ActiveEmployees or Resignations — same convention as
    // ScorecardService.HistoricalRecordsCacheKey.
    public const string HistoricalRecordsCacheKey = "early-warning:historical-records";
    public const string ResignedEmployeeIdsCacheKey = "early-warning:resigned-employee-ids";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    // LoadActiveCandidatesAsync's result does not depend on store/role — only on
    // months/year — but GetWatchlistAsync's own cache key includes store, so
    // calling it once per store (e.g. StoreActionPlanService's per-period
    // detection loop, ~200 stores) reran this every time with an identical
    // result. Caching it separately, keyed only by months/year, turns ~200
    // redundant full ActiveEmployees pulls into one.
    private static string CandidatesCacheKey(string? months, int? year) => $"early-warning:candidates_{months}_{year}";

    // Same problem as LoadActiveCandidatesAsync above: these two full-table
    // reads (ExitInterviews, StoreReferences) don't vary by store/role, but sat
    // uncached inside GetWatchlistAsync's body — whose own cache key includes
    // store — so a per-store caller (StoreActionPlanService's detection loop)
    // re-ran them once per store instead of once per run.
    private const string StoreExitScoresCacheKey = "early-warning:store-exit-scores";
    private const string StoreReferenceRowsCacheKey = "early-warning:store-reference-rows";

    // Short-lived cache for the fully-scored watchlist itself, keyed by its
    // request parameters (same convention as DashboardService.GetKpisAsync/
    // GetStoreComparisonAsync). GetSummaryAsync recomputes the exact same
    // watchlist internally (same store/role/assignedName/months/year every
    // time it's called with the same filters), so without this every Early
    // Warning page load ran the whole per-employee scoring pass — including a
    // fresh, uncached pull of every active employee for the anchor period —
    // twice: once for the watchlist request, once inside the summary request.
    private static readonly TimeSpan WatchlistCacheDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContext;
    public EarlyWarningService(AppDbContext db, IStoreAccessService storeAccess, IMemoryCache cache, IHttpContextAccessor httpContext)
    {
        _db = db;
        _storeAccess = storeAccess;
        _cache = cache;
        _httpContext = httpContext;
    }

    private List<string>? RequestedJobs =>
        MultiValueFilter.Split(_httpContext.HttpContext?.Request.Query["jobs"].ToString());

    // ── Constants ─────────────────────────────────────────────────────────────
    private const int DefaultNewHireWindowDays = 90;
    private const int MinNewHireWindowDays     = 30;
    private const int MaxNewHireWindowDays     = 180;
    private const int PeakBucketWidthDays      = 30;
    private const int MinRateSampleSize        = 10;
    private const int MinPeakSampleSize        = 5;
    private const int MinExitSampleSize        = 3;
    /// <summary>Floor for StdDev to prevent over-sensitivity when data is homogeneous.</summary>
    private const double MinStdDev = 5.0;

    // ── Internal DTOs ─────────────────────────────────────────────────────────
    private class HistoricalRecord
    {
        public string Store    { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string Gender   { get; set; } = "";
        public DateOnly HireDate { get; set; }
        /// <summary>Null = still active (never resigned).</summary>
        public int? TenureDays { get; set; }
    }

    private class ActiveCandidate
    {
        public string   EmployeeId { get; set; } = "";
        public string   Name       { get; set; } = "";
        public string   Store      { get; set; } = "";
        public string   JobTitle   { get; set; } = "";
        public string   Gender     { get; set; } = "";
        public DateOnly HireDate   { get; set; }
        public int      TenureDays { get; set; }
    }

    // ── Data loaders ──────────────────────────────────────────────────────────
    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
        // This query has no request-specific parameters — same rows regardless of
        // filters — so it's cached whole rather than re-read from the database on every
        // call. See UploadService for the write-side invalidation of
        // HistoricalRecordsCacheKey.
        if (_cache.TryGetValue(HistoricalRecordsCacheKey, out List<HistoricalRecord>? cachedRecords) && cachedRecords != null)
            return cachedRecords;

        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.JobTitle, e.Gender, e.HireDate })
            .ToListAsync();

        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.JobTitle, r.Gender, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, HistoricalRecord>();

        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new HistoricalRecord
            {
                Store = a.Store, JobTitle = a.JobTitle, Gender = a.Gender, HireDate = a.HireDate!.Value, TenureDays = null,
            };
        }

        // Resignation records win — they prove the employee actually left.
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store      = r.Store,
                JobTitle   = r.JobTitle,
                Gender     = r.Gender,
                HireDate   = r.HireDate!.Value,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }

        var records = byEmployee.Values.ToList();
        _cache.Set(HistoricalRecordsCacheKey, records, CacheDuration);
        return records;
    }

    // Same no-request-parameters cacheability as LoadHistoricalRecordsAsync above.
    private async Task<List<string>> LoadResignedEmployeeIdsAsync()
    {
        if (_cache.TryGetValue(ResignedEmployeeIdsCacheKey, out List<string>? cached) && cached != null)
            return cached;

        var ids = await _db.Resignations.Select(r => r.EmployeeId).Distinct().ToListAsync();
        _cache.Set(ResignedEmployeeIdsCacheKey, ids, CacheDuration);
        return ids;
    }

    private async Task<(List<ActiveCandidate> Candidates, int Month, int Year)> LoadActiveCandidatesAsync(string? months, int? year)
    {
        var candidatesCacheKey = CandidatesCacheKey(months, year);
        if (_cache.TryGetValue(candidatesCacheKey, out (List<ActiveCandidate> Candidates, int Month, int Year) cached))
            return cached;

        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0)
        {
            var empty = (new List<ActiveCandidate>(), 0, 0);
            _cache.Set(candidatesCacheKey, empty, WatchlistCacheDuration);
            return empty;
        }

        (int Month, int Year) anchor;
        if (year.HasValue)
        {
            var resolved = DashboardService.ResolvePeriods(null, year, null, null, months)
                .Where(p => periods.Any(x => x.Month == p.Month && x.Year == p.Year))
                .ToList();
            anchor = resolved.Count > 0
                ? resolved.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First()
                : periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Month, p.Year)).First();
        }
        else
        {
            anchor = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Month, p.Year)).First();
        }

        var asOf       = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1);
        var resignedIds = (await LoadResignedEmployeeIdsAsync()).ToHashSet();

        var rows = await _db.ActiveEmployees
            .Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Name, e.Store, e.JobTitle, e.Gender, e.HireDate })
            .ToListAsync();

        var candidates = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeId) && !resignedIds.Contains(r.EmployeeId))
            .Select(r => new ActiveCandidate
            {
                EmployeeId = r.EmployeeId,
                Name       = r.Name,
                Store      = r.Store,
                JobTitle   = r.JobTitle,
                Gender     = r.Gender,
                HireDate   = r.HireDate!.Value,
                TenureDays = asOf.DayNumber - r.HireDate!.Value.DayNumber,
            })
            .ToList();

        var result = (candidates, anchor.Month, anchor.Year);
        _cache.Set(candidatesCacheKey, result, WatchlistCacheDuration);
        return result;
    }

    // ── Statistical helpers ───────────────────────────────────────────────────

    private static double CalcStdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
        var mean = list.Average();
        return Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / list.Count);
    }

    /// <summary>
    /// Returns graduated score 1-3 based on how many effective StdDevs
    /// the <paramref name="rate"/> exceeds <paramref name="mean"/>.
    /// Returns 0 if not significant.
    /// </summary>
    private static int GraduatedScore(double rate, double mean, double stddev)
    {
        var eff = Math.Max(stddev, MinStdDev);
        if (rate >= mean + 2.0 * eff) return 3;
        if (rate >= mean + 1.5 * eff) return 2;
        if (rate >= mean + 1.0 * eff) return 1;
        return 0;
    }

    /// <summary>
    /// Groups historical records by <paramref name="keySelector"/>, computes early-leave
    /// rate per group, and returns the rate dictionary plus the distribution's mean and StdDev.
    /// Groups with fewer than <see cref="MinRateSampleSize"/> records are excluded.
    /// </summary>
    private static (Dictionary<string, double> Rates, double Mean, double Std) ComputeGroupRates(
        List<HistoricalRecord> historical,
        Func<HistoricalRecord, string> keySelector,
        int windowDays = MetricsCalculationService.NinetyDayWindowDays)
    {
        var valid = historical
            .Where(h => !string.IsNullOrWhiteSpace(keySelector(h)))
            .GroupBy(keySelector)
            .Select(g =>
            {
                var list = g.ToList();
                if (list.Count < MinRateSampleSize) return (Key: (string?)null, Rate: 0.0);
                var rate = MetricsCalculationService.RatePercent(list.Count(h => h.TenureDays != null && h.TenureDays <= windowDays), list.Count);
                return (Key: g.Key, Rate: rate);
            })
            .Where(x => x.Key != null)
            .Select(x => (x.Key!, x.Rate))
            .ToList();

        var rates  = valid.ToDictionary(x => x.Item1, x => x.Item2);
        var allRates = valid.Select(x => x.Item2).ToList();
        return (rates, allRates.Count > 0 ? allRates.Average() : 0, CalcStdDev(allRates));
    }

    /// <summary>
    /// Computes a role-specific new-hire-window length from the median resignation tenure
    /// of employees in that role. Falls back to <see cref="DefaultNewHireWindowDays"/>.
    /// </summary>
    private static Dictionary<string, int> ComputeRoleWindows(List<HistoricalRecord> historical)
    {
        return historical
            .Where(h => h.TenureDays != null && !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .Where(g => g.Count() >= MinRateSampleSize)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var tenures = g.Where(h => h.TenureDays.HasValue)
                                   .Select(h => h.TenureDays!.Value)
                                   .OrderBy(x => x)
                                   .ToList();
                    if (tenures.Count == 0) return DefaultNewHireWindowDays;
                    var median = tenures[tenures.Count / 2];
                    return Math.Clamp(median, MinNewHireWindowDays, MaxNewHireWindowDays);
                });
    }

    /// <summary>
    /// Returns a score 1-3 for being inside the new-hire window;
    /// higher the closer the employee is to the end of the window.
    /// </summary>
    private static int NewHireWindowScore(int tenureDays, int windowDays)
    {
        if (tenureDays > windowDays) return 0;
        var progress = (double)tenureDays / windowDays;
        if (progress >= 0.75) return 3; // last 25 % — most at-risk
        if (progress >= 0.40) return 2; // mid stretch
        return 1;
    }

    private static (int Start, int End)? ComputePeakBucket(IEnumerable<int> lateTenures, int windowDays)
    {
        var list = lateTenures.ToList();
        if (list.Count < MinPeakSampleSize) return null;
        var best = list
            .GroupBy(t => (t - windowDays - 1) / PeakBucketWidthDays)
            .OrderByDescending(g => g.Count())
            .First();
        var start = windowDays + best.Key * PeakBucketWidthDays;
        return (start, start + PeakBucketWidthDays);
    }

    /// <summary>
    /// Arabic Likert/agree-disagree heuristic — same logic as ExitInterviewService.
    /// </summary>
    private static int ExitSentiment(string answer)
    {
        var a = answer.Trim();
        if (a.Contains("لا أوافق ولا أعارض") || a.Contains("محايد") || a == "مقبولة") return 0;
        if (a.Contains("أعارض") || a.Contains("ضعيف") || a == "لا")                  return -1;
        if (a.Contains("أوافق") || a == "جيدة" || a == "نعم"
            || a.Contains("كبيرة") || a.Contains("عالية"))                             return 1;
        return 0;
    }

    /// <summary>
    /// Computes negative-sentiment % per store from exit interviews.
    /// Considers five key engagement dimensions.
    /// </summary>
    private async Task<Dictionary<string, double>> LoadStoreExitScoresAsync()
    {
        if (_cache.TryGetValue(StoreExitScoresCacheKey, out Dictionary<string, double>? cached) && cached != null)
            return cached;

        var rows = await _db.ExitInterviews
            .Where(e => !string.IsNullOrEmpty(e.Store))
            .Select(e => new
            {
                e.Store,
                e.WorkloadCondition, e.FairTreatment,
                e.Communication,     e.Training,
                e.ComplaintsHandling,
            })
            .ToListAsync();

        var result = rows
            .GroupBy(e => e.Store)
            .Where(g => g.Count() >= MinExitSampleSize)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var answers = g.SelectMany(e => new[]
                    {
                        e.WorkloadCondition, e.FairTreatment,
                        e.Communication,     e.Training,
                        e.ComplaintsHandling,
                    })
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

                    if (answers.Count == 0) return 0.0;
                    return answers.Count(a => ExitSentiment(a) < 0) * 100.0 / answers.Count;
                });

        _cache.Set(StoreExitScoresCacheKey, result, WatchlistCacheDuration);
        return result;
    }

    // Raw rows behind both LoadStoreLeaderRatesAsync and GetWatchlistAsync's
    // Operation-Consultant-per-store lookup — same underlying StoreReferences
    // table, previously queried twice (with slightly different filters) on
    // every call.
    private async Task<List<(string StoreName, string StoreLeader, string OperationConsultant, string OperationManager, string SeniorOperationConsultant, string OperationDirector, int Year, int Month)>> LoadStoreReferenceRowsAsync()
    {
        if (_cache.TryGetValue(StoreReferenceRowsCacheKey, out List<(string, string, string, string, string, string, int, int)>? cached) && cached != null)
            return cached;

        var rows = await _db.StoreReferences
            .Where(s => !string.IsNullOrEmpty(s.StoreName))
            .Select(s => new
            {
                s.StoreName,
                s.StoreLeader,
                s.OperationConsultant,
                s.OperationManager,
                s.SeniorOperationConsultant,
                s.OperationDirector,
                s.Year,
                s.Month
            })
            .ToListAsync();

        var result = rows.Select(s => (
            s.StoreName,
            s.StoreLeader,
            s.OperationConsultant,
            s.OperationManager,
            s.SeniorOperationConsultant,
            s.OperationDirector,
            s.Year,
            s.Month
        )).ToList();
        _cache.Set(StoreReferenceRowsCacheKey, result, WatchlistCacheDuration);
        return result;
    }

    /// <summary>
    /// For each active store, resolves the current store leader and computes their
    /// historical early-leave rate across all stores they have ever managed.
    /// </summary>
    private async Task<Dictionary<string, (string Leader, double LeaderRate)>> LoadStoreLeaderRatesAsync(
        List<HistoricalRecord> historical)
    {
        var storeRefs = (await LoadStoreReferenceRowsAsync())
            .Where(s => !string.IsNullOrEmpty(s.StoreLeader) && !string.IsNullOrEmpty(s.StoreName))
            .Select(s => new { s.StoreName, s.StoreLeader, s.Year, s.Month })
            .ToList();

        if (storeRefs.Count == 0) return [];

        // Most recent leader per store
        var currentLeaders = storeRefs
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First().StoreLeader);

        // All stores each leader has ever managed
        var leaderStores = storeRefs
            .GroupBy(s => s.StoreLeader)
            .ToDictionary(g => g.Key, g => g.Select(s => s.StoreName).Distinct().ToHashSet());

        // Historical resignation stats per store
        var histByStore = historical
            .Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => (
                Total:      g.Count(),
                EarlyLeave: g.Count(h => MetricsCalculationService.IsEarlyLeaver(h.TenureDays))));

        // Weighted early-leave rate per leader across all their stores
        var leaderRates = new Dictionary<string, double>();
        foreach (var (leader, stores) in leaderStores)
        {
            int total = 0, earlyLeave = 0;
            foreach (var s in stores)
                if (histByStore.TryGetValue(s, out var hr)) { total += hr.Total; earlyLeave += hr.EarlyLeave; }
            if (total >= MinRateSampleSize)
                leaderRates[leader] = MetricsCalculationService.RatePercent(earlyLeave, total);
        }

        // Map store → (leader, rate)
        var result = new Dictionary<string, (string, double)>();
        foreach (var (store, leader) in currentLeaders)
            if (leaderRates.TryGetValue(leader, out var rate))
                result[store] = (leader, rate);

        return result;
    }

    /// <summary>
    /// Maps a raw cumulative score to a 1-5 star display value. Boundaries are
    /// calibrated against the actual RiskScore distribution (see
    /// --diagnose-early-warning-scores) rather than the theoretical max: across
    /// the live Watchlist, RiskScore tops out at 7 and P95 is 4, so "High Risk"
    /// (Stars >= 4, used by the Early Warning page's High Risk table/charts/
    /// summary) is scoped to score >= 5 — the top ~5% of the Watchlist — instead
    /// of the old score >= 7 cutoff, which only 2 of 2661 employees ever reached.
    /// </summary>
    private static int ScoreToStars(int score) => score switch
    {
        <= 1  => 1,
        <= 3  => 2,
        <= 4  => 3,
        <= 6  => 4,
        _     => 5,
    };

    // ── Public API ────────────────────────────────────────────────────────────
    public async Task<List<string>> GetStoreListAsync(string role, string? assignedName)
    {
        var (candidates, _, _) = await LoadActiveCandidatesAsync(null, null);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var stores = candidates.Select(c => c.Store).Where(s => !string.IsNullOrWhiteSpace(s));
        if (accessible != null) stores = stores.Where(s => accessible.Contains(s));
        return stores.Distinct().OrderBy(s => s).ToList();
    }

    public async Task<List<EarlyWarningItem>> GetWatchlistAsync(
        string? store, string role, string? assignedName, string? months = null, int? year = null,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var cacheKey = $"early-warning:watchlist_{store}_{role}_{assignedName}_{months}_{year}_{om}_{oc}_{soc}_{od}_{string.Join(',', RequestedJobs ?? new())}";
        if (_cache.TryGetValue(cacheKey, out List<EarlyWarningItem>? cachedWatchlist) && cachedWatchlist != null)
            return cachedWatchlist;

        // ── Load raw data ─────────────────────────────────────────────────────
        var historical = await LoadHistoricalRecordsAsync();
        var (candidatesRaw, anchorMonth, anchorYear) = await LoadActiveCandidatesAsync(months, year);
        var candidates = candidatesRaw;
        if (RequestedJobs is { } jobs)
        {
            historical = historical.Where(h => jobs.Contains(h.JobTitle)).ToList();
            candidates = candidates.Where(c => jobs.Contains(c.JobTitle)).ToList();
        }

        // Role-based store access is always applied — never bypassed by the
        // explicit store filter, which only narrows further on top of it.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) candidates = candidates.Where(c => accessible.Contains(c.Store)).ToList();
        if (MultiValueFilter.Split(store) is { } stores)
            candidates = candidates.Where(c => stores.Contains(c.Store)).ToList();

        // ── Company-wide 90-day baseline ──────────────────────────────────────
        // Gated to the go-live cohort and using the same (early leavers ÷ new
        // hires) formula as NinetyDayTurnoverService/ScorecardService, so the
        // "Company Baseline" figure shown here matches the 90-Day Turnover page.
        var goLiveHistorical = historical.Where(h => MetricsCalculationService.IsPastGoLive(h.HireDate)).ToList();
        var companyTotal    = goLiveHistorical.Count;
        var companyEarly    = goLiveHistorical.Count(h => MetricsCalculationService.IsEarlyLeaver(h.TenureDays));
        var companyRate     = MetricsCalculationService.NinetyDayRate(companyTotal, companyEarly);

        // ── Role-specific new-hire windows (uses full history — a distinct,
        //    long-run heuristic for sizing the "new hire" window itself,
        //    not the 90-day rate) ──────────────────────────────────────────
        var roleWindows = ComputeRoleWindows(historical);

        // ── Rate distributions with StdDev thresholds (go-live-gated) ────────
        var (storeRates,  storeMean,  storeStd)  = ComputeGroupRates(goLiveHistorical, h => h.Store);
        var (roleRates,   roleMean,   roleStd)   = ComputeGroupRates(goLiveHistorical, h => h.JobTitle);
        var (genderRates, genderMean, genderStd) = ComputeGroupRates(goLiveHistorical, h => h.Gender);

        // ── Peak windows (store → role → company fallback) ────────────────────
        var lateHistorical = historical
            .Where(h => h.TenureDays != null && h.TenureDays > DefaultNewHireWindowDays)
            .ToList();
        var companyPeakBucket = ComputePeakBucket(lateHistorical.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays);
        var storePeakBuckets  = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays));
        var rolePeakBuckets   = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value), DefaultNewHireWindowDays));

        // ── Exit interview store health ───────────────────────────────────────
        var exitScores  = await LoadStoreExitScoresAsync();
        var exitMean    = exitScores.Count > 0 ? exitScores.Values.Average() : 0;
        var exitStd     = Math.Max(CalcStdDev(exitScores.Values), MinStdDev);

        // ── Store leader history ──────────────────────────────────────────────
        var leaderRates     = await LoadStoreLeaderRatesAsync(goLiveHistorical);
        var allLeaderRates  = leaderRates.Values.Select(x => x.LeaderRate).ToList();
        var leaderMean      = allLeaderRates.Count > 0 ? allLeaderRates.Average() : companyRate;
        var leaderStd       = Math.Max(CalcStdDev(allLeaderRates), MinStdDev);

        // ── Operation Consultant per store — latest assignment at or before the
        //    anchor period, so it matches the same snapshot the candidates come
        //    from (falls back to the latest known assignment if none is ≤ anchor).
        var storeRefRows = await LoadStoreReferenceRowsAsync();
        var leadershipByStore = storeRefRows
            .GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g =>
            {
                var upToAnchor = g.Where(s => s.Year < anchorYear || (s.Year == anchorYear && s.Month <= anchorMonth)).ToList();
                return (upToAnchor.Count > 0 ? upToAnchor : g.ToList())
                    .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First();
            });
        var ocByStore = leadershipByStore.ToDictionary(
            x => x.Key,
            x => x.Value.OperationConsultant ?? "");

        if (MultiValueFilter.Split(om) is { } oms)
            candidates = candidates.Where(c => leadershipByStore.TryGetValue(c.Store, out var l) && oms.Contains(l.OperationManager ?? "")).ToList();
        if (MultiValueFilter.Split(oc) is { } ocs)
            candidates = candidates.Where(c => leadershipByStore.TryGetValue(c.Store, out var l) && ocs.Contains(l.OperationConsultant ?? "")).ToList();
        if (MultiValueFilter.Split(soc) is { } socs)
            candidates = candidates.Where(c => leadershipByStore.TryGetValue(c.Store, out var l) && socs.Contains(l.SeniorOperationConsultant ?? "")).ToList();
        if (MultiValueFilter.Split(od) is { } ods)
            candidates = candidates.Where(c => leadershipByStore.TryGetValue(c.Store, out var l) && ods.Contains(l.OperationDirector ?? "")).ToList();

        // ── Score each active employee ────────────────────────────────────────
        var result = new List<EarlyWarningItem>();

        foreach (var c in candidates)
        {
            var reasons    = new List<EarlyWarningReason>();
            var roleWindow = roleWindows.TryGetValue(c.JobTitle, out var rw) ? rw : DefaultNewHireWindowDays;

            // 1 ── New Hire Window (graduated 1-3, role-specific window)
            var nhScore = NewHireWindowScore(c.TenureDays, roleWindow);
            if (nhScore > 0)
                reasons.Add(new EarlyWarningReason
                {
                    Type  = "new_hire_window",
                    Score = nhScore,
                    Params = new() { ["days"] = c.TenureDays.ToString(), ["window"] = roleWindow.ToString() },
                });

            // 2 ── Store History (graduated 1-3)
            if (storeRates.TryGetValue(c.Store, out var sRate))
            {
                var sScore = GraduatedScore(sRate, storeMean, storeStd);
                if (sScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "store_history",
                        Score = sScore,
                        Params = new() { ["store"] = c.Store, ["rate"] = sRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 3 ── Role History (graduated 1-3)
            if (roleRates.TryGetValue(c.JobTitle, out var rRate))
            {
                var rScore = GraduatedScore(rRate, roleMean, roleStd);
                if (rScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "role_history",
                        Score = rScore,
                        Params = new() { ["jobTitle"] = c.JobTitle, ["rate"] = rRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 4 ── Peak Resignation Window (1 = approaching, 2 = inside)
            var peakBucket = (storePeakBuckets.TryGetValue(c.Store, out var sb) ? sb : null)
                          ?? (rolePeakBuckets.TryGetValue(c.JobTitle, out var rb) ? rb : null)
                          ?? companyPeakBucket;
            if (peakBucket.HasValue && c.TenureDays > DefaultNewHireWindowDays)
            {
                var inCore    = c.TenureDays >= peakBucket.Value.Start && c.TenureDays <= peakBucket.Value.End;
                var approach  = c.TenureDays >= peakBucket.Value.Start - PeakBucketWidthDays
                             && c.TenureDays <  peakBucket.Value.Start;
                if (inCore || approach)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "peak_window",
                        Score = inCore ? 2 : 1,
                        Params = new() { ["start"] = peakBucket.Value.Start.ToString(), ["end"] = peakBucket.Value.End.ToString() },
                    });
            }

            // 5 ── Gender History (1 point if gender group is significantly high-risk)
            if (!string.IsNullOrWhiteSpace(c.Gender) && genderRates.TryGetValue(c.Gender, out var gRate))
            {
                if (GraduatedScore(gRate, genderMean, genderStd) > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "gender_history",
                        Score = 1,
                        Params = new() { ["gender"] = c.Gender, ["rate"] = gRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                    });
            }

            // 6 ── Exit Interview Store Score (1-2)
            if (exitScores.TryGetValue(c.Store, out var negRate) && negRate > 0)
            {
                var eiScore = GraduatedScore(negRate, exitMean, exitStd) >= 2 ? 2
                            : GraduatedScore(negRate, exitMean, exitStd) == 1 ? 1
                            : 0;
                if (eiScore > 0)
                    reasons.Add(new EarlyWarningReason
                    {
                        Type  = "exit_interview_score",
                        Score = eiScore,
                        Params = new() { ["store"] = c.Store, ["negRate"] = negRate.ToString("F0") },
                    });
            }

            // 7 ── Store Leader History (1 point)
            if (leaderRates.TryGetValue(c.Store, out var li)
                && GraduatedScore(li.LeaderRate, leaderMean, leaderStd) > 0)
                reasons.Add(new EarlyWarningReason
                {
                    Type  = "store_leader_history",
                    Score = 1,
                    Params = new() { ["leader"] = li.Leader, ["rate"] = li.LeaderRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                });

            if (reasons.Count == 0) continue;

            var totalScore = reasons.Sum(r => r.Score);
            result.Add(new EarlyWarningItem
            {
                Name      = c.Name,
                Store     = c.Store,
                OperationConsultant = ocByStore.TryGetValue(c.Store, out var ocVal) ? ocVal : "",
                JobTitle  = c.JobTitle,
                HireDate  = c.HireDate,
                TenureDays = c.TenureDays,
                RiskScore = totalScore,
                Stars     = ScoreToStars(totalScore),
                Reasons   = reasons,
            });
        }

        var watchlist = result.OrderByDescending(r => r.RiskScore).ThenBy(r => r.TenureDays).ToList();
        _cache.Set(cacheKey, watchlist, WatchlistCacheDuration);
        return watchlist;
    }

    public async Task<EarlyWarningSummary> GetSummaryAsync(
        string? store, string role, string? assignedName, string? months = null, int? year = null,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        // Same go-live-gated (early leavers ÷ new hires) formula as GetWatchlistAsync,
        // NinetyDayTurnoverService, and ScorecardService — kept in sync, including
        // the Job filter, so this baseline moves the same way GetWatchlistAsync's
        // own (embedded) companyRate does.
        var historical = await LoadHistoricalRecordsAsync();
        if (RequestedJobs is { } jobs) historical = historical.Where(h => jobs.Contains(h.JobTitle)).ToList();
        var goLiveHistorical = historical.Where(h => MetricsCalculationService.IsPastGoLive(h.HireDate)).ToList();
        var companyTotal = goLiveHistorical.Count;
        var companyEarly = goLiveHistorical.Count(h => MetricsCalculationService.IsEarlyLeaver(h.TenureDays));
        var companyRate  = MetricsCalculationService.NinetyDayRate(companyTotal, companyEarly);

        var list = await GetWatchlistAsync(store, role, assignedName, months, year, om, oc, soc, od);
        return new EarlyWarningSummary
        {
            TotalWatchlist      = list.Count,
            HighRiskCount       = list.Count(r => r.Stars >= 4),
            NewHireWindowCount  = list.Count(r => r.Reasons.Any(x => x.Type == "new_hire_window")),
            CompanyBaselineRate = companyRate,
        };
    }
}
