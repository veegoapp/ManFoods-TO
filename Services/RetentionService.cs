using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class RetentionService : IRetentionService
{
    private readonly AppDbContext _db;

    public RetentionService(AppDbContext db) => _db = db;

    // Real "retention" is a long-term measure — 30/90-day attrition is covered by the
    // dedicated 90-Day Turnover page instead.
    private static readonly (int Days, string Label)[] Milestones =
    {
        (182, "6 Months"), (365, "1 Year"), (730, "2 Years"), (1095, "3 Years"), (1460, "4 Years"), (1825, "5 Years")
    };
    private static readonly (int Days, string Label)[] CurvePoints =
    {
        (0, "Day 0"), (30, "1mo"), (90, "3mo"), (182, "6mo"), (365, "1yr"), (545, "1.5yr"), (730, "2yr"), (1095, "3yr"), (1460, "4yr"), (1825, "5yr")
    };
    private const int LeaderboardDays = 365; // 1-year retention, the standard HR benchmark
    private static readonly (string Label, int Min, int Max)[] TenureBuckets =
    {
        ("< 6 months", 0, 182),
        ("6–12 months", 182, 365),
        ("1–2 years", 365, 730),
        ("2–3 years", 730, 1095),
        ("3–4 years", 1095, 1460),
        ("4–5 years", 1460, 1825),
        ("5+ years", 1825, int.MaxValue),
    };

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
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null)
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

        if (!string.IsNullOrWhiteSpace(months) && toYear.HasValue)
        {
            var keys = DashboardService.ResolvePeriods(toMonth, toYear, fromMonth, fromYear, months)
                .Select(p => p.Year * 100 + p.Month).ToHashSet();
            cohorts = cohorts.Where(c => keys.Contains(c.CohortYear * 100 + c.CohortMonth));
        }
        else if (fromMonth.HasValue && fromYear.HasValue && toMonth.HasValue && toYear.HasValue)
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
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc, months);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var result = new List<RetentionMilestoneItem>();
        foreach (var (days, label) in Milestones)
        {
            var included = cohorts.Where(c => CohortReaches(c.CohortMonth, c.CohortYear, days)).ToList();
            if (included.Count == 0)
            {
                result.Add(new RetentionMilestoneItem { Days = days, Label = label });
                continue;
            }
            var total = included.Count;
            var retained = included.Count(c => c.TenureDays == null || c.TenureDays > days);
            var latest = included.OrderByDescending(c => c.CohortYear).ThenByDescending(c => c.CohortMonth).First();
            result.Add(new RetentionMilestoneItem
            {
                Days = days,
                Label = label,
                RetentionRate = Math.Round(retained * 100.0 / total, 1),
                TotalHires = total,
                Retained = retained,
                ThroughCohortLabel = new DateOnly(latest.CohortYear, latest.CohortMonth, 1).ToString("MMM yyyy"),
            });
        }
        return result;
    }

    public async Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc, months);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var result = new List<SurvivalPoint>();
        foreach (var (day, label) in CurvePoints)
        {
            var included = cohorts.Where(c => CohortReaches(c.CohortMonth, c.CohortYear, day)).ToList();
            if (included.Count == 0) continue;
            var total = included.Count;
            var retained = included.Count(c => c.TenureDays == null || c.TenureDays > day);
            result.Add(new SurvivalPoint
            {
                Day = day,
                Label = label,
                RetentionRate = Math.Round(retained * 100.0 / total, 1),
                SampleSize = total,
            });
        }
        return result;
    }

    public async Task<List<RetentionTrendPoint>> GetTrendAsync(string? store, string? om = null, string? oc = null, int? sinceYear = null)
    {
        // Always full history (like the Turnover page's Monthly Trend) — unaffected
        // by the discrete cohort-month filter used for the milestone cards above.
        var cohorts = await LoadEmployeeCohortsAsync(om: om, oc: oc);
        if (store != null) cohorts = cohorts.Where(c => c.Store == store).ToList();

        var periods = cohorts.Select(c => (c.CohortMonth, c.CohortYear))
            .Distinct()
            .Where(p => !sinceYear.HasValue || p.CohortYear >= sinceYear.Value)
            .OrderBy(p => p.CohortYear).ThenBy(p => p.CohortMonth)
            .ToList();

        var result = new List<RetentionTrendPoint>();
        foreach (var (month, year) in periods)
        {
            var cohortRows = cohorts.Where(c => c.CohortMonth == month && c.CohortYear == year).ToList();
            var total = cohortRows.Count;
            if (total == 0) continue;

            var point = new RetentionTrendPoint { Label = new DateOnly(year, month, 1).ToString("MMM yy") };
            foreach (var (days, label) in Milestones)
            {
                point.Rates[label] = Math.Round(cohortRows.Count(c => c.TenureDays == null || c.TenureDays > days) * 100.0 / total, 1);
                point.Provisional[label] = !CohortReaches(month, year, days);
            }
            result.Add(point);
        }
        return result;
    }

    public async Task<List<ChartDataItem>> GetStoreLeaderboardAsync(
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(fromMonth, fromYear, toMonth, toYear, om, oc, months);
        var included = cohorts
            .Where(c => !string.IsNullOrWhiteSpace(c.Store) && CohortReaches(c.CohortMonth, c.CohortYear, LeaderboardDays))
            .ToList();

        return included.GroupBy(c => c.Store)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)Math.Round(g.Count(c => c.TenureDays == null || c.TenureDays > LeaderboardDays) * 100.0 / g.Count()),
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

        return TenureBuckets
            .Select(b => new ChartDataItem
            {
                Label = b.Label,
                Value = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max),
            })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<StoreTenureRow>> GetTenureDistributionByStoreAsync(string? store, string? om = null, string? oc = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<StoreTenureRow>();
        var latest = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == latest.Month && e.Year == latest.Year && e.HireDate != null);
        if (store != null) rowsQuery = rowsQuery.Where(e => e.Store == store);
        else if (await GetStoresForOmOcAsync(om, oc) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var rows = await rowsQuery.Select(e => new { e.Store, e.HireDate }).ToListAsync();

        var asOf = new DateOnly(latest.Year, latest.Month, DateTime.DaysInMonth(latest.Year, latest.Month));

        return rows.GroupBy(r => r.Store)
            .Select(g => new StoreTenureRow
            {
                StoreName = g.Key,
                Headcount = g.Count(),
                Buckets = TenureBuckets.Select(b => new ChartDataItem
                {
                    Label = b.Label,
                    Value = g.Count(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) >= b.Min && (asOf.DayNumber - x.HireDate!.Value.DayNumber) < b.Max)
                }).ToList()
            })
            .OrderByDescending(r => r.Headcount)
            .ToList();
    }

    public async Task<List<SmartInsightItem>> GetInsightsAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();
        const string milestoneKey = "1 Year";

        // 1. Recent vs. prior 1-year retention trend (up to 3 complete cohorts each side,
        // full history — not limited to the page's cohort-month filter).
        var trend = await GetTrendAsync(store, om, oc);
        var complete = trend.Where(t => t.Rates.TryGetValue(milestoneKey, out var r) && r.HasValue && !t.Provisional[milestoneKey]).ToList();
        if (complete.Count >= 2)
        {
            var recent = complete.TakeLast(Math.Min(3, complete.Count)).ToList();
            var priorCount = Math.Min(3, complete.Count - recent.Count);
            if (priorCount > 0)
            {
                var prior = complete.Skip(complete.Count - recent.Count - priorCount).Take(priorCount).ToList();
                var recentAvg = recent.Average(t => t.Rates[milestoneKey]!.Value);
                var priorAvg = prior.Average(t => t.Rates[milestoneKey]!.Value);
                var diff = Math.Round(recentAvg - priorAvg, 1);
                if (Math.Abs(diff) >= 1)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = diff > 0 ? "bi-arrow-up-circle-fill" : "bi-arrow-down-circle-fill",
                        Color = diff > 0 ? "success" : "danger",
                        Title = diff > 0 ? "1-Year Retention Improving" : "1-Year Retention Slipping",
                        Description = $"{recentAvg:F1}% avg over the last {recent.Count} cohort(s) vs {priorAvg:F1}% before — {(diff > 0 ? "+" : "")}{diff}pt.",
                    });
            }
        }

        // 2. Best/worst store on 1-year retention (only meaningful company-wide).
        if (store == null)
        {
            var leaderboard = await GetStoreLeaderboardAsync(fromMonth, fromYear, toMonth, toYear, om, oc, months);
            if (leaderboard.Count > 0)
            {
                var best = leaderboard.First();
                insights.Add(new SmartInsightItem
                {
                    Icon = "bi-trophy-fill",
                    Color = "success",
                    Title = $"Best 1-Year Retention: {best.Label}",
                    Description = $"{best.Value}% of hires are still there after 1 year.",
                });
                var worst = leaderboard.Last();
                if (worst.Label != best.Label && worst.Value < 50)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = "bi-exclamation-triangle-fill",
                        Color = "danger",
                        Title = $"Weakest 1-Year Retention: {worst.Label}",
                        Description = $"Only {worst.Value}% of hires are still there after 1 year.",
                    });
            }
        }

        // 3. Workforce maturity from the active-employee tenure distribution.
        var tenureDist = await GetTenureDistributionAsync(store, om, oc);
        var totalActive = tenureDist.Sum(t => t.Value);
        if (totalActive > 0)
        {
            var seasoned = tenureDist.Where(t => t.Label is "1–2 years" or "2–3 years" or "3–4 years" or "4–5 years" or "5+ years").Sum(t => t.Value);
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
