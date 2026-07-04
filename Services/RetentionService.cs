using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class RetentionService : IRetentionService
{
    private readonly AppDbContext _db;

    public RetentionService(AppDbContext db) => _db = db;

    private static readonly int[] MilestoneDays = { 30, 90, 180, 365 };
    private static readonly int[] CurveDays = { 0, 7, 30, 60, 90, 120, 180, 270, 365 };

    private class EmployeeCohort
    {
        public string EmployeeId { get; set; } = "";
        public string Store { get; set; } = "";
        public int CohortMonth { get; set; }
        public int CohortYear { get; set; }
        /// <summary>Null means still active (never resigned) as of the latest upload.</summary>
        public int? TenureDays { get; set; }
    }

    // Stores whose latest-known Operation Manager / Operation Consultant match the filter.
    // Returns null when no OM/OC filter is set.
    private async Task<List<string>?> GetStoresForOmOcAsync(string? om, string? oc)
    {
        if (string.IsNullOrEmpty(om) && string.IsNullOrEmpty(oc)) return null;
        var refs = await _db.StoreReferences.ToListAsync();
        var latestByStore = refs.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        return latestByStore.Values
            .Where(s => (string.IsNullOrEmpty(om) || s.OperationManager == om)
                     && (string.IsNullOrEmpty(oc) || s.OperationConsultant == oc))
            .Select(s => s.StoreName)
            .ToList();
    }

    private async Task<List<EmployeeCohort>> LoadEmployeeCohortsAsync(
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();

        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, EmployeeCohort>();

        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new EmployeeCohort
            {
                EmployeeId = a.EmployeeId,
                Store = a.Store,
                CohortMonth = a.HireDate!.Value.Month,
                CohortYear = a.HireDate!.Value.Year,
                TenureDays = null,
            };
        }

        // Resignation records win — they prove the employee actually left.
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new EmployeeCohort
            {
                EmployeeId = r.EmployeeId,
                Store = r.Store,
                CohortMonth = r.HireDate!.Value.Month,
                CohortYear = r.HireDate!.Value.Year,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }

        IEnumerable<EmployeeCohort> cohorts = byEmployee.Values;

        if (fromMonth.HasValue && fromYear.HasValue && toMonth.HasValue && toYear.HasValue)
        {
            var keys = DashboardService.ExpandRangeKeys(fromMonth.Value, fromYear.Value, toMonth.Value, toYear.Value).ToHashSet();
            cohorts = cohorts.Where(c => keys.Contains(c.CohortYear * 100 + c.CohortMonth));
        }

        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        if (omOcStores != null) cohorts = cohorts.Where(c => omOcStores.Contains(c.Store));

        return cohorts.ToList();
    }

    private static DateOnly CohortCloseDate(int month, int year) =>
        new(year, month, DateTime.DaysInMonth(year, month));

    private static bool CohortReaches(int month, int year, int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow) >= CohortCloseDate(month, year).AddDays(days);

    public async Task<List<string>> GetStoreListAsync()
    {
        var cohorts = await LoadEmployeeCohortsAsync();
        return cohorts.Select(c => c.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var result = new List<RetentionMilestoneItem>();
        foreach (var days in MilestoneDays)
        {
            var included = cohorts.Where(c => CohortReaches(c.CohortMonth, c.CohortYear, days)).ToList();
            if (included.Count == 0)
            {
                result.Add(new RetentionMilestoneItem { Days = days });
                continue;
            }
            var total = included.Count;
            var retained = included.Count(c => c.TenureDays == null || c.TenureDays > days);
            var latest = included.OrderByDescending(c => c.CohortYear).ThenByDescending(c => c.CohortMonth).First();
            result.Add(new RetentionMilestoneItem
            {
                Days = days,
                RetentionRate = Math.Round(retained * 100.0 / total, 1),
                TotalHires = total,
                Retained = retained,
                ThroughCohortLabel = new DateOnly(latest.CohortYear, latest.CohortMonth, 1).ToString("MMM yyyy"),
            });
        }
        return result;
    }

    public async Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var result = new List<SurvivalPoint>();
        foreach (var day in CurveDays)
        {
            var included = cohorts.Where(c => CohortReaches(c.CohortMonth, c.CohortYear, day)).ToList();
            if (included.Count == 0) continue;
            var total = included.Count;
            var retained = included.Count(c => c.TenureDays == null || c.TenureDays > day);
            result.Add(new SurvivalPoint
            {
                Day = day,
                RetentionRate = Math.Round(retained * 100.0 / total, 1),
                SampleSize = total,
            });
        }
        return result;
    }

    public async Task<List<RetentionTrendPoint>> GetTrendAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var periods = cohorts.Select(c => (c.CohortMonth, c.CohortYear))
            .Distinct()
            .OrderBy(p => p.CohortYear).ThenBy(p => p.CohortMonth)
            .ToList();

        var result = new List<RetentionTrendPoint>();
        foreach (var (month, year) in periods)
        {
            var cohortRows = cohorts.Where(c => c.CohortMonth == month && c.CohortYear == year).ToList();
            var total = cohortRows.Count;
            if (total == 0) continue;

            double Rate(int days) => Math.Round(cohortRows.Count(c => c.TenureDays == null || c.TenureDays > days) * 100.0 / total, 1);

            result.Add(new RetentionTrendPoint
            {
                Label = new DateOnly(year, month, 1).ToString("MMM yy"),
                Retention90 = Rate(90),
                Provisional90 = !CohortReaches(month, year, 90),
                Retention180 = Rate(180),
                Provisional180 = !CohortReaches(month, year, 180),
                Retention365 = Rate(365),
                Provisional365 = !CohortReaches(month, year, 365),
            });
        }
        return result;
    }

    public async Task<List<ChartDataItem>> GetStoreLeaderboardAsync(
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        const int days = 180;
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc);
        var included = cohorts
            .Where(c => !string.IsNullOrWhiteSpace(c.Store) && CohortReaches(c.CohortMonth, c.CohortYear, days))
            .ToList();

        return included.GroupBy(c => c.Store)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)Math.Round(g.Count(c => c.TenureDays == null || c.TenureDays > days) * 100.0 / g.Count()),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string? om = null, string? oc = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<ChartDataItem>();
        var latest = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == latest.Month && e.Year == latest.Year && e.HireDate != null);
        if (store != null) rowsQuery = rowsQuery.Where(e => e.Store == store);
        else if (await GetStoresForOmOcAsync(om, oc) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var hireDates = await rowsQuery.Select(e => e.HireDate!.Value).ToListAsync();

        var asOf = new DateOnly(latest.Year, latest.Month, DateTime.DaysInMonth(latest.Year, latest.Month));
        var buckets = new (string Label, int Min, int Max)[]
        {
            ("< 3 months", 0, 90),
            ("3–6 months", 90, 180),
            ("6–12 months", 180, 365),
            ("1–2 years", 365, 730),
            ("2+ years", 730, int.MaxValue),
        };

        return buckets
            .Select(b => new ChartDataItem
            {
                Label = b.Label,
                Value = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max),
            })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<SmartInsightItem>> GetInsightsAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null)
    {
        var insights = new List<SmartInsightItem>();

        // 1. Recent vs. prior 90-day retention trend (up to 3 complete cohorts each side).
        var trend = await GetTrendAsync(store, fromMonth, fromYear, toMonth, toYear, om, oc);
        var complete90 = trend.Where(t => !t.Provisional90 && t.Retention90 != null).ToList();
        if (complete90.Count >= 2)
        {
            var recent = complete90.TakeLast(Math.Min(3, complete90.Count)).ToList();
            var priorCount = Math.Min(3, complete90.Count - recent.Count);
            if (priorCount > 0)
            {
                var prior = complete90.Skip(complete90.Count - recent.Count - priorCount).Take(priorCount).ToList();
                var recentAvg = recent.Average(t => t.Retention90!.Value);
                var priorAvg = prior.Average(t => t.Retention90!.Value);
                var diff = Math.Round(recentAvg - priorAvg, 1);
                if (Math.Abs(diff) >= 1)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = diff > 0 ? "bi-arrow-up-circle-fill" : "bi-arrow-down-circle-fill",
                        Color = diff > 0 ? "success" : "danger",
                        Title = diff > 0 ? "90-Day Retention Improving" : "90-Day Retention Slipping",
                        Description = $"{recentAvg:F1}% avg over the last {recent.Count} cohort(s) vs {priorAvg:F1}% before — {(diff > 0 ? "+" : "")}{diff}pt.",
                    });
            }
        }

        // 2. Best/worst store on 180-day retention (only meaningful company-wide).
        if (store == null)
        {
            var leaderboard = await GetStoreLeaderboardAsync(fromMonth, fromYear, toMonth, toYear, om, oc);
            if (leaderboard.Count > 0)
            {
                var best = leaderboard.First();
                insights.Add(new SmartInsightItem
                {
                    Icon = "bi-trophy-fill",
                    Color = "success",
                    Title = $"Best 180-Day Retention: {best.Label}",
                    Description = $"{best.Value}% of hires are still there 180 days later.",
                });
                var worst = leaderboard.Last();
                if (worst.Label != best.Label && worst.Value < 70)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = "bi-exclamation-triangle-fill",
                        Color = "danger",
                        Title = $"Weakest 180-Day Retention: {worst.Label}",
                        Description = $"Only {worst.Value}% of hires are still there 180 days later.",
                    });
            }
        }

        // 3. Workforce maturity from the active-employee tenure distribution.
        var tenureDist = await GetTenureDistributionAsync(store, om, oc);
        var totalActive = tenureDist.Sum(t => t.Value);
        if (totalActive > 0)
        {
            var seasoned = tenureDist.Where(t => t.Label is "1–2 years" or "2+ years").Sum(t => t.Value);
            var pct = Math.Round(seasoned * 100.0 / totalActive, 0);
            insights.Add(new SmartInsightItem
            {
                Icon = "bi-shield-check",
                Color = pct >= 40 ? "success" : "secondary",
                Title = "Workforce Maturity",
                Description = $"{pct}% of the current active workforce has been here a year or more.",
            });
        }

        return insights;
    }
}
