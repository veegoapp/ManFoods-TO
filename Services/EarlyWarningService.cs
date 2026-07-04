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

    private async Task<List<ActiveCandidate>> LoadActiveCandidatesAsync()
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<ActiveCandidate>();
        var latest = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var resignedIds = (await _db.Resignations.Select(r => r.EmployeeId).Distinct().ToListAsync()).ToHashSet();

        var rows = await _db.ActiveEmployees
            .Where(e => e.Month == latest.Month && e.Year == latest.Year && e.HireDate != null)
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
            })
            .ToList();
    }

    public async Task<List<string>> GetStoreListAsync()
    {
        var candidates = await LoadActiveCandidatesAsync();
        return candidates.Select(c => c.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store)
    {
        var historical = await LoadHistoricalRecordsAsync();
        var candidates = await LoadActiveCandidatesAsync();
        if (store != null) candidates = candidates.Where(c => c.Store == store).ToList();

        var companyTotal = historical.Count;
        var companyEarly = historical.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays);
        var companyRate = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        double RateFor(IEnumerable<HistoricalRecord> group)
        {
            var list = group.ToList();
            return list.Count > 0 ? list.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays) * 100.0 / list.Count : 0;
        }

        var storeRates = historical.Where(h => !string.IsNullOrWhiteSpace(h.Store))
            .GroupBy(h => h.Store)
            .ToDictionary(g => g.Key, g => RateFor(g));

        var roleRates = historical.Where(h => !string.IsNullOrWhiteSpace(h.JobTitle))
            .GroupBy(h => h.JobTitle)
            .ToDictionary(g => g.Key, g => RateFor(g));

        // Peak resignation window beyond the initial 90-day window, company-wide.
        var lateTenures = historical.Where(h => h.TenureDays != null && h.TenureDays > NewHireWindowDays)
            .Select(h => h.TenureDays!.Value)
            .ToList();
        (int Start, int End)? peakBucket = null;
        if (lateTenures.Count >= 5)
        {
            var best = lateTenures
                .GroupBy(t => (t - NewHireWindowDays - 1) / PeakBucketWidthDays)
                .OrderByDescending(g => g.Count())
                .First();
            var start = NewHireWindowDays + best.Key * PeakBucketWidthDays;
            peakBucket = (start, start + PeakBucketWidthDays);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<EarlyWarningItem>();

        foreach (var c in candidates)
        {
            var tenureDays = today.DayNumber - c.HireDate.DayNumber;
            var reasons = new List<string>();

            if (tenureDays <= NewHireWindowDays)
                reasons.Add($"لسه في أول {NewHireWindowDays} يوم من التعيين ({tenureDays} يوم) — أعلى فترة خطر تاريخيًا");

            if (companyTotal > 0 && storeRates.TryGetValue(c.Store, out var sRate) && sRate >= companyRate + RiskMarginPoints)
                reasons.Add($"فرع {c.Store} تاريخيًا معدل ترك العمل المبكر فيه {sRate:F0}% (المتوسط العام {companyRate:F0}%)");

            if (companyTotal > 0 && roleRates.TryGetValue(c.JobTitle, out var rRate) && rRate >= companyRate + RiskMarginPoints)
                reasons.Add($"وظيفة {c.JobTitle} تاريخيًا معدل ترك العمل المبكر فيها {rRate:F0}% (المتوسط العام {companyRate:F0}%)");

            if (peakBucket.HasValue && tenureDays > NewHireWindowDays &&
                tenureDays >= peakBucket.Value.Start - PeakBucketWidthDays && tenureDays <= peakBucket.Value.End)
                reasons.Add($"قرب يوصل لفترة كان فيها استقالات كتير تاريخيًا (حوالي {peakBucket.Value.Start}-{peakBucket.Value.End} يوم من التعيين)");

            if (reasons.Count == 0) continue;

            result.Add(new EarlyWarningItem
            {
                Name = c.Name,
                Store = c.Store,
                JobTitle = c.JobTitle,
                HireDate = c.HireDate,
                TenureDays = tenureDays,
                RiskScore = reasons.Count,
                Reasons = reasons,
            });
        }

        return result.OrderByDescending(r => r.RiskScore).ThenBy(r => r.TenureDays).ToList();
    }

    public async Task<EarlyWarningSummary> GetSummaryAsync(string? store)
    {
        var historical = await LoadHistoricalRecordsAsync();
        var companyTotal = historical.Count;
        var companyEarly = historical.Count(h => h.TenureDays != null && h.TenureDays <= NewHireWindowDays);
        var companyRate = companyTotal > 0 ? companyEarly * 100.0 / companyTotal : 0;

        var list = await GetWatchlistAsync(store);
        return new EarlyWarningSummary
        {
            TotalWatchlist = list.Count,
            HighRiskCount = list.Count(r => r.RiskScore >= 2),
            NewHireWindowCount = list.Count(r => r.Reasons.Any(x => x.Contains($"أول {NewHireWindowDays} يوم"))),
            CompanyBaselineRate = Math.Round(companyRate, 1),
        };
    }
}
