using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class EarlyWarningService : IEarlyWarningService
{
    private readonly AppDbContext _db;

    public EarlyWarningService(AppDbContext db) => _db = db;

    private const int NewHireWindowDays = 90;
    private const double RiskMarginPoints = 10; // store/role rate must exceed company avg by this many points to flag
    private const int PeakBucketWidthDays = 30;
    private const int MinRateSampleSize = 10;   // minimum historical records before trusting a store/role early-leave rate
    private const int MinPeakSampleSize = 5;    // minimum late-leaver records before trusting a peak-window bucket

    private class HistoricalRecord
    {
        public string Store { get; set; } = "";
        public string JobTitle { get; set; } = "";
        /// <summary>Null means still active (never resigned) as of the latest upload.</summary>
        public int? TenureDays { get; set; }
    }

    private class ActiveCandidate
    {
        public string EmployeeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Store { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public DateOnly HireDate { get; set; }
        public int TenureDays { get; set; }
    }

    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
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
            byEmployee[a.EmployeeId] = new HistoricalRecord { Store = a.Store, JobTitle = a.JobTitle, TenureDays = null };
        }

        // Resignation records win — they prove the employee actually left.
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store = r.Store,
                JobTitle = r.JobTitle,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }

        return byEmployee.Values.ToList();
    }

    // "months"/"year" pick which Active Employees period is treated as "currently active"
    // (defaults to the latest uploaded period) — tenure is measured as of the last day of
    // that period so the numbers stay internally consistent when looking at a past period.
    private async Task<List<ActiveCandidate>> LoadActiveCandidatesAsync(string? months, int? year)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<ActiveCandidate>();

        (int Month, int Year) anchor;
        if (year.HasValue && !string.IsNullOrWhiteSpace(months))
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

        var asOf = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1);
        var resignedIds = (await _db.Resignations.Select(r => r.EmployeeId).Distinct().ToListAsync()).ToHashSet();

        var rows = await _db.ActiveEmployees
            .Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Name, e.Store, e.JobTitle, e.HireDate })
            .ToListAsync();

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeId) && !resignedIds.Contains(r.EmployeeId))
            .Select(r => new ActiveCandidate
            {
                EmployeeId = r.EmployeeId,
                Name = r.Name,
                Store = r.Store,
                JobTitle = r.JobTitle,
                HireDate = r.HireDate!.Value,
                TenureDays = asOf.DayNumber - r.HireDate!.Value.DayNumber,
            })
            .ToList();
    }

    public async Task<List<string>> GetStoreListAsync()
    {
        var candidates = await LoadActiveCandidatesAsync(null, null);
        return candidates.Select(c => c.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    private static (int Start, int End)? ComputePeakBucket(IEnumerable<int> lateTenures)
    {
        var list = lateTenures.ToList();
        if (list.Count < MinPeakSampleSize) return null;
        var best = list
            .GroupBy(t => (t - NewHireWindowDays - 1) / PeakBucketWidthDays)
            .OrderByDescending(g => g.Count())
            .First();
        var start = NewHireWindowDays + best.Key * PeakBucketWidthDays;
        return (start, start + PeakBucketWidthDays);
    }

    public async Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store, string? months = null, int? year = null)
    {
        var historical = await LoadHistoricalRecordsAsync();
        var candidates = await LoadActiveCandidatesAsync(months, year);
        if (!string.IsNullOrWhiteSpace(store)) candidates = candidates.Where(c => c.Store == store).ToList();

        var companyTotal = historical.Count;
        var companyEarly = historical.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays);
        var companyRate = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        // A store/role early-leave rate only counts as a risk signal once it has
        // enough historical records behind it — otherwise a handful of hires at a
        // small store can swing the "rate" wildly and produce a false alarm.
        static double? RateFor(IEnumerable<HistoricalRecord> group)
        {
            var list = group.ToList();
            if (list.Count < MinRateSampleSize) return null;
            return list.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays) * 100.0 / list.Count;
        }

        var storeRates = historical.Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .Select(g => (g.Key, Rate: RateFor(g)))
            .Where(x => x.Rate.HasValue)
            .ToDictionary(x => x.Key, x => x.Rate!.Value);

        var roleRates = historical.Where(h => !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .Select(g => (g.Key, Rate: RateFor(g)))
            .Where(x => x.Rate.HasValue)
            .ToDictionary(x => x.Key, x => x.Rate!.Value);

        // Peak resignation window beyond the initial 90-day window. Prefer a
        // store-specific bucket, then a role-specific one, and only fall back to
        // the single company-wide bucket when neither has enough samples —
        // different stores/roles genuinely tend to lose people at different points.
        var lateHistorical = historical.Where(h => h.TenureDays != null && h.TenureDays > NewHireWindowDays).ToList();
        var companyPeakBucket = ComputePeakBucket(lateHistorical.Select(h => h.TenureDays!.Value));
        var storePeakBuckets = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value)));
        var rolePeakBuckets = lateHistorical.Where(h => !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .ToDictionary(g => g.Key, g => ComputePeakBucket(g.Select(h => h.TenureDays!.Value)));

        var result = new List<EarlyWarningItem>();

        foreach (var c in candidates)
        {
            var reasons = new List<EarlyWarningReason>();

            if (c.TenureDays <= NewHireWindowDays)
                reasons.Add(new EarlyWarningReason
                {
                    Type = "new_hire_window",
                    Params = new() { ["days"] = c.TenureDays.ToString(), ["window"] = NewHireWindowDays.ToString() },
                });

            if (storeRates.TryGetValue(c.Store, out var sRate) && sRate >= companyRate + RiskMarginPoints)
                reasons.Add(new EarlyWarningReason
                {
                    Type = "store_history",
                    Params = new() { ["store"] = c.Store, ["rate"] = sRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                });

            if (roleRates.TryGetValue(c.JobTitle, out var rRate) && rRate >= companyRate + RiskMarginPoints)
                reasons.Add(new EarlyWarningReason
                {
                    Type = "role_history",
                    Params = new() { ["jobTitle"] = c.JobTitle, ["rate"] = rRate.ToString("F0"), ["companyRate"] = companyRate.ToString("F0") },
                });

            var peakBucket = (storePeakBuckets.TryGetValue(c.Store, out var sb) ? sb : null)
                ?? (rolePeakBuckets.TryGetValue(c.JobTitle, out var rb) ? rb : null)
                ?? companyPeakBucket;
            if (peakBucket.HasValue && c.TenureDays > NewHireWindowDays &&
                c.TenureDays >= peakBucket.Value.Start - PeakBucketWidthDays && c.TenureDays <= peakBucket.Value.End)
                reasons.Add(new EarlyWarningReason
                {
                    Type = "peak_window",
                    Params = new() { ["start"] = peakBucket.Value.Start.ToString(), ["end"] = peakBucket.Value.End.ToString() },
                });

            if (reasons.Count == 0) continue;

            result.Add(new EarlyWarningItem
            {
                Name = c.Name,
                Store = c.Store,
                JobTitle = c.JobTitle,
                HireDate = c.HireDate,
                TenureDays = c.TenureDays,
                RiskScore = reasons.Count,
                Reasons = reasons,
            });
        }

        return result.OrderByDescending(r => r.RiskScore).ThenBy(r => r.TenureDays).ToList();
    }

    public async Task<EarlyWarningSummary> GetSummaryAsync(string? store, string? months = null, int? year = null)
    {
        var historical = await LoadHistoricalRecordsAsync();
        var companyTotal = historical.Count;
        var companyEarly = historical.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays);
        var companyRate = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        var list = await GetWatchlistAsync(store, months, year);
        return new EarlyWarningSummary
        {
            TotalWatchlist = list.Count,
            HighRiskCount = list.Count(r => r.RiskScore >= 2),
            NewHireWindowCount = list.Count(r => r.Reasons.Any(x => x.Type == "new_hire_window")),
            CompanyBaselineRate = Math.Round(companyRate, 1),
        };
    }
}
