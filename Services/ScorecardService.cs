using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ScorecardService : IScorecardService
{
    private readonly AppDbContext _db;
    private readonly IExitInterviewService _exitInterviews;

    public ScorecardService(AppDbContext db, IExitInterviewService exitInterviews)
    {
        _db = db;
        _exitInterviews = exitInterviews;
    }

    private static string Pick(StoreReference s, string dimension) => dimension switch
    {
        "leader" => s.StoreLeader,
        "oc" => s.OperationConsultant,
        "om" => s.OperationManager,
        _ => "",
    };

    private class NameAggregate
    {
        public HashSet<string> Stores { get; } = new();
        public Dictionary<(int Month, int Year), int> PeriodHeadcount { get; } = new();
        public int TotalResignations;
        public string LatestOc = "";
        public string LatestOm = "";
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
        string dimension, string? om, string? oc, string? months, int? year)
    {
        var periods = DashboardService.ResolvePeriods(null, year, null, null, months);
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        if (periodKeys.Count == 0) return new Dictionary<string, NameAggregate>();

        var storeRefs = await _db.StoreReferences.Where(s => periodKeys.Contains(s.Year * 100 + s.Month)).ToListAsync();
        if (!string.IsNullOrEmpty(om)) storeRefs = storeRefs.Where(s => s.OperationManager == om).ToList();
        if (!string.IsNullOrEmpty(oc)) storeRefs = storeRefs.Where(s => s.OperationConsultant == oc).ToList();

        var headcountMap = (await _db.ActiveEmployees.Where(e => periodKeys.Contains(e.Year * 100 + e.Month))
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        var resignMap = (await _db.Resignations.Where(r => periodKeys.Contains(r.Year * 100 + r.Month))
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
        }
        return result;
    }

    private class HistoricalRecord
    {
        public string Store { get; set; } = "";
        public int? TenureDays { get; set; }
        /// <summary>YYYYMM key derived from the employee's hire date — used to scope
        /// early-leaver / retention rates to the selected period.</summary>
        public int HireDateKey { get; set; }
    }

    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();
        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, HistoricalRecord>();
        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new HistoricalRecord
            {
                Store       = a.Store,
                TenureDays  = null,
                HireDateKey = a.HireDate!.Value.Year * 100 + a.HireDate!.Value.Month,
            };
        }
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store       = r.Store,
                TenureDays  = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
                HireDateKey = r.HireDate!.Value.Year * 100 + r.HireDate!.Value.Month,
            };
        }
        return byEmployee.Values.ToList();
    }

    public async Task<List<ScorecardRow>> GetScorecardAsync(string dimension, string? om = null, string? oc = null, string? months = null, int? year = null)
    {
        // Resolve once so both aggregates and the historical period window use
        // the same effective year (latest data year when caller passes no year).
        var effectiveYear = await ResolveEffectiveYearAsync(year);

        var aggregates = await BuildNameAggregatesAsync(dimension, om, oc, months, effectiveYear);
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

        var result = new List<ScorecardRow>();
        // Sequential — EF Core DbContext does not support concurrent queries.
        foreach (var (name, agg) in aggregates)
        {
            var avgHeadcount = agg.PeriodHeadcount.Count > 0 ? agg.PeriodHeadcount.Values.Average() : 0;
            var turnoverRate = avgHeadcount > 0 ? Math.Round(agg.TotalResignations * 100.0 / avgHeadcount, 1) : 0;

            var records = periodFiltered.Where(h => agg.Stores.Contains(h.Store)).ToList();
            var total = records.Count;
            var early90 = total > 0 ? Math.Round(records.Count(r => r.TenureDays != null && r.TenureDays <= 90) * 100.0 / total, 1) : 0;
            var retained180 = total > 0 ? Math.Round(records.Count(r => r.TenureDays == null || r.TenureDays > 180) * 100.0 / total, 1) : 0;

            var filter = dimension switch
            {
                "leader" => new ExitInterviewFilter { StoreLeader = name },
                "oc" => new ExitInterviewFilter { OperationConsultant = name },
                "om" => new ExitInterviewFilter { OperationManager = name },
                _ => new ExitInterviewFilter(),
            };
            var sentiment = await _exitInterviews.GetSentimentSummaryAsync(filter, "Admin", null);

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

    public async Task<List<string>> GetLeaderNamesAsync() =>
        await _db.StoreReferences.Where(s => s.StoreLeader != "")
            .Select(s => s.StoreLeader).Distinct().OrderBy(s => s).ToListAsync();

    public async Task<List<LeaderHistoryRow>> GetLeaderHistoryAsync(string leaderName, string? months = null, int? year = null)
    {
        if (string.IsNullOrWhiteSpace(leaderName)) return new List<LeaderHistoryRow>();

        var periods = DashboardService.ResolvePeriods(null, year, null, null, months);
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        if (periodKeys.Count == 0) return new List<LeaderHistoryRow>();

        var rows = await _db.StoreReferences
            .Where(s => s.StoreLeader == leaderName && periodKeys.Contains(s.Year * 100 + s.Month))
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync();
        if (rows.Count == 0) return new List<LeaderHistoryRow>();

        var headcountMap = (await _db.ActiveEmployees.Where(e => periodKeys.Contains(e.Year * 100 + e.Month))
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

        var resignMap = (await _db.Resignations.Where(r => periodKeys.Contains(r.Year * 100 + r.Month))
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => (x.Store, x.Month, x.Year), x => x.Count);

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
                TurnoverRate = hc > 0 ? Math.Round(res * 100.0 / hc, 1) : 0,
                IsStoreTransition = previousStore != null && previousStore != r.StoreName,
            });
            previousStore = r.StoreName;
        }
        return result;
    }

    public async Task<ScorecardRollupResult> GetRollupAsync(string? months = null, int? year = null)
    {
        var result = new ScorecardRollupResult();
        var leaderAggregates = await BuildNameAggregatesAsync("leader", null, null, months, year);
        if (leaderAggregates.Count == 0) return result;

        var leaderRates = leaderAggregates.Select(kv =>
        {
            var avgHeadcount = kv.Value.PeriodHeadcount.Count > 0 ? kv.Value.PeriodHeadcount.Values.Average() : 0;
            var rate = avgHeadcount > 0 ? kv.Value.TotalResignations * 100.0 / avgHeadcount : 0;
            return (Name: kv.Key, Rate: rate, Oc: kv.Value.LatestOc, Om: kv.Value.LatestOm);
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

        return result;
    }

    private static RollupRow BuildRollupRow(string name, List<(string Name, double Rate, string Oc, string Om)> leaders, double average)
    {
        var flagged = leaders.Count(l => l.Rate > average);
        return new RollupRow
        {
            Name = name,
            TotalLeaders = leaders.Count,
            FlaggedLeaders = flagged,
            FlaggedPercent = leaders.Count > 0 ? Math.Round(flagged * 100.0 / leaders.Count, 1) : 0,
        };
    }
}
