using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class NinetyDayTurnoverService : INinetyDayTurnoverService
{
    private readonly AppDbContext _db;

    public NinetyDayTurnoverService(AppDbContext db) => _db = db;

    private class ResignationTenure
    {
        public string EmployeeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Store { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string PayrollGroup { get; set; } = "";
        public string Gender { get; set; } = "";
        public DateOnly HireDate { get; set; }
        public DateOnly ResignationDate { get; set; }
        public int TenureDays { get; set; }
    }

    private async Task<List<(string EmployeeId, string Store, int Month, int Year)>> LoadActiveHiresAsync()
    {
        var rows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null && e.HireDate.Value.Year >= 2026)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();
        return rows.Select(r => (r.EmployeeId, r.Store, r.HireDate!.Value.Month, r.HireDate!.Value.Year)).ToList();
    }

    private async Task<List<ResignationTenure>> LoadResignationTenuresAsync()
    {
        var rows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null && r.HireDate.Value.Year >= 2026)
            .ToListAsync();
        return rows.Select(r => new ResignationTenure
        {
            EmployeeId = r.EmployeeId,
            Name = r.Name,
            Store = r.Store,
            JobTitle = r.JobTitle,
            PayrollGroup = r.PayrollGroup,
            Gender = r.Gender,
            HireDate = r.HireDate!.Value,
            ResignationDate = r.ResignationDate!.Value,
            TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
        }).ToList();
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

    private static NinetyDayKpiViewModel ComputeKpi(
        List<(string EmployeeId, string Store, int Month, int Year)> activeHires,
        List<ResignationTenure> resTenures,
        HashSet<int> cohortKeys, int latestMonth, int latestYear, List<string>? stores, List<string>? omOcStores)
    {
        bool StoreOk(string s) => (stores == null || stores.Contains(s)) && (omOcStores == null || omOcStores.Contains(s));

        var fromActive = activeHires.Where(a => cohortKeys.Contains(a.Year * 100 + a.Month) && StoreOk(a.Store)).Select(a => a.EmployeeId);
        var fromRes = resTenures.Where(r => cohortKeys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && StoreOk(r.Store)).Select(r => r.EmployeeId);
        var hireIds = fromActive.Concat(fromRes).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToHashSet();

        var earlyLeaverIds = resTenures
            .Where(r => cohortKeys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && r.TenureDays <= 90 && StoreOk(r.Store))
            .Select(r => r.EmployeeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToHashSet();

        var totalHires = hireIds.Count;
        var earlyLeavers = earlyLeaverIds.Count;
        var rate = totalHires > 0 ? Math.Round(earlyLeavers * 100.0 / totalHires, 1) : 0;

        var cohortCloseDate = new DateOnly(latestYear, latestMonth, DateTime.DaysInMonth(latestYear, latestMonth));
        var isProvisional = DateOnly.FromDateTime(DateTime.UtcNow) < cohortCloseDate.AddDays(90);

        return new NinetyDayKpiViewModel
        {
            CohortMonth = latestMonth,
            CohortYear = latestYear,
            TotalHires = totalHires,
            EarlyLeavers = earlyLeavers,
            Rate = rate,
            IsProvisional = isProvisional,
        };
    }

    public async Task<List<PeriodItem>> GetCohortPeriodsAsync()
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();

        return activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodItem { Month = p.Month, Year = p.Year })
            .ToList();
    }

    public async Task<List<string>> GetStoreListAsync()
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        return activeHires.Select(a => a.Store)
            .Concat(resTenures.Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<NinetyDayKpiViewModel> GetKpiAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        return ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, MultiValueFilter.Split(store), omOcStores);
    }

    public async Task<List<RateTrendItem>> GetTrendAsync(string? store, string? om = null, string? oc = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        var stores = MultiValueFilter.Split(store);

        var periods = activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();

        var result = new List<RateTrendItem>();
        foreach (var (m, y) in periods)
        {
            var kpi = ComputeKpi(activeHires, resTenures, new HashSet<int> { y * 100 + m }, m, y, stores, omOcStores);
            if (kpi.TotalHires == 0) continue;
            result.Add(new RateTrendItem
            {
                Label = new DateOnly(y, m, 1).ToString("MMM yy"),
                Rate = kpi.Rate,
                TotalHires = kpi.TotalHires,
                EarlyLeavers = kpi.EarlyLeavers,
                IsProvisional = kpi.IsProvisional,
            });
        }
        return result;
    }

    public async Task<List<ChartDataItem>> GetByStoreAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);

        var stores = activeHires.Where(a => keys.Contains(a.Year * 100 + a.Month)).Select(a => a.Store)
            .Concat(resTenures.Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month)).Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        if (omOcStores != null) stores = stores.Where(s => omOcStores.Contains(s));

        var result = new List<ChartDataItem>();
        foreach (var store in stores)
        {
            var kpi = ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, new List<string> { store }, null);
            if (kpi.TotalHires == 0) continue;
            result.Add(new ChartDataItem { Label = store, Value = (int)Math.Round(kpi.Rate) });
        }
        return result.OrderByDescending(c => c.Value).ToList();
    }

    public async Task<List<NinetyDayStoreRow>> GetStoreComparisonAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);

        var stores = activeHires.Where(a => keys.Contains(a.Year * 100 + a.Month)).Select(a => a.Store)
            .Concat(resTenures.Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month)).Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        if (omOcStores != null) stores = stores.Where(s => omOcStores.Contains(s));

        var storeRefList = await _db.StoreReferences.ToListAsync();
        var latestRefByStore = storeRefList.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());

        var rows = new List<NinetyDayStoreRow>();
        foreach (var store in stores)
        {
            var kpi = ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, new List<string> { store }, null);
            if (kpi.TotalHires == 0) continue;
            latestRefByStore.TryGetValue(store, out var sr);
            rows.Add(new NinetyDayStoreRow
            {
                StoreName           = store,
                OperationConsultant = sr?.OperationConsultant ?? "",
                OperationManager    = sr?.OperationManager ?? "",
                TotalHires          = kpi.TotalHires,
                EarlyLeavers        = kpi.EarlyLeavers,
                Rate                = kpi.Rate,
            });
        }
        return rows.OrderByDescending(r => r.Rate).ToList();
    }

    public async Task<NinetyDayOcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var stores = await GetStoreComparisonAsync(month, year, fromMonth, fromYear, om, oc, months);

        NinetyDayOcOmRow ToRow(IGrouping<string, NinetyDayStoreRow> g, string type) => new()
        {
            Name         = g.Key,
            Type         = type,
            StoreCount   = g.Count(),
            TotalHires   = g.Sum(s => s.TotalHires),
            EarlyLeavers = g.Sum(s => s.EarlyLeavers),
            AvgRate      = g.Sum(s => s.TotalHires) > 0
                ? Math.Round(g.Sum(s => s.EarlyLeavers) * 100.0 / g.Sum(s => s.TotalHires), 1)
                : 0,
        };

        var ocRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => ToRow(g, "OC"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        var omRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => ToRow(g, "OM"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        return new NinetyDayOcOmAnalysisResult { OcRows = ocRows, OmRows = omRows };
    }

    public async Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var keys = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months)
            .Select(p => p.Year * 100 + p.Month).ToHashSet();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        var stores = MultiValueFilter.Split(store);
        return resTenures
            .Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && r.TenureDays <= 90
                     && (stores == null || stores.Contains(r.Store))
                     && (omOcStores == null || omOcStores.Contains(r.Store)))
            .OrderBy(r => r.TenureDays)
            .Select(r => new EarlyLeaverRow
            {
                Name = r.Name,
                Store = r.Store,
                JobTitle = r.JobTitle,
                HireDate = r.HireDate,
                ResignationDate = r.ResignationDate,
                TenureDays = r.TenureDays,
            })
            .ToList();
    }

    // Early leavers (TenureDays <= 90) whose hire cohort and store fall within
    // the resolved filter — shared by every "early leaver breakdown" chart.
    private async Task<List<ResignationTenure>> EarlyLeaversAsync(int month, int year, string? store,
        int? fromMonth, int? fromYear, string? om, string? oc, string? months)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var keys = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months)
            .Select(p => p.Year * 100 + p.Month).ToHashSet();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        var stores = MultiValueFilter.Split(store);
        return resTenures
            .Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && r.TenureDays <= 90
                     && (stores == null || stores.Contains(r.Store))
                     && (omOcStores == null || omOcStores.Contains(r.Store)))
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverJobTitlesAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, fromMonth, fromYear, om, oc, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.JobTitle))
            .GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverPayrollGroupsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, fromMonth, fromYear, om, oc, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.PayrollGroup))
            .GroupBy(r => r.PayrollGroup)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverGenderBreakdownAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, fromMonth, fromYear, om, oc, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.Gender))
            .GroupBy(r => r.Gender)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, fromMonth, fromYear, om, oc, months);
        var earlyLeaverIds = leavers
            .Select(r => r.EmployeeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (earlyLeaverIds.Count == 0) return new List<ChartDataItem>();

        var reasons = await _db.ExitInterviews
            .Where(e => earlyLeaverIds.Contains(e.EmployeeId) && e.ReasonForLeaving != "")
            .Select(e => e.ReasonForLeaving)
            .ToListAsync();

        return reasons
            .GroupBy(r => r)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string? om = null, string? oc = null, string? months = null, int? sinceYear = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        var monthFilter = MultiValueFilter.Split(months)?.Select(int.Parse).ToHashSet();

        var periods = activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .Where(p => !sinceYear.HasValue || p.Year >= sinceYear.Value)
            .Where(p => monthFilter == null || monthFilter.Contains(p.Month))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();
        var periodKeys = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        var allStores = activeHires.Select(a => a.Store)
            .Concat(resTenures.Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (omOcStores != null) allStores = allStores.Where(s => omOcStores.Contains(s)).ToList();

        var storeRefList = await _db.StoreReferences.ToListAsync();
        var latestRefByStore = storeRefList.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        var ocByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationConsultant ?? "");
        var omByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationManager ?? "");

        var rows = allStores.Select(store =>
        {
            var periodRates = new Dictionary<string, double?>();
            var nonNullRates = new List<double>();
            var storeList = new List<string> { store };

            foreach (var (m, y) in periods)
            {
                var pk = $"{y:D4}-{m:D2}";
                var kpi = ComputeKpi(activeHires, resTenures, new HashSet<int> { y * 100 + m }, m, y, storeList, null);
                if (kpi.TotalHires > 0)
                {
                    periodRates[pk] = kpi.Rate;
                    nonNullRates.Add(kpi.Rate);
                }
                else
                {
                    periodRates[pk] = null;
                }
            }

            return new TrendMatrixRow
            {
                StoreName           = store,
                OperationConsultant = ocByStore.TryGetValue(store, out var ocVal) ? ocVal : "",
                OperationManager    = omByStore.TryGetValue(store, out var omVal) ? omVal : "",
                PeriodRates         = periodRates,
                AvgRate             = nonNullRates.Count > 0 ? Math.Round(nonNullRates.Average(), 1) : null,
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();

        // 1. Recent vs. prior 90-day rate trend (up to 3 complete cohorts each
        // side, full history — not limited to the page's cohort-month filter).
        var trend = await GetTrendAsync(store, om, oc);
        var complete = trend.Where(t => !t.IsProvisional).ToList();
        if (complete.Count >= 2)
        {
            var recent = complete.TakeLast(Math.Min(3, complete.Count)).ToList();
            var priorCount = Math.Min(3, complete.Count - recent.Count);
            if (priorCount > 0)
            {
                var prior = complete.Skip(complete.Count - recent.Count - priorCount).Take(priorCount).ToList();
                var recentAvg = recent.Average(t => t.Rate);
                var priorAvg = prior.Average(t => t.Rate);
                var diff = Math.Round(recentAvg - priorAvg, 1);
                if (Math.Abs(diff) >= 1)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = diff < 0 ? "bi-arrow-down-circle-fill" : "bi-arrow-up-circle-fill",
                        Color = diff < 0 ? "success" : "danger",
                        Title = diff < 0 ? "90-Day Rate Improving" : "90-Day Rate Slipping",
                        Description = $"{recentAvg:F1}% avg over the last {recent.Count} cohort(s) vs {priorAvg:F1}% before — {(diff > 0 ? "+" : "")}{diff}pt.",
                    });
            }
        }

        // 2. Best/worst store for the selected cohort months (only meaningful company-wide).
        if (store == null)
        {
            var byStore = await GetStoreComparisonAsync(month, year, fromMonth, fromYear, om, oc, months);
            if (byStore.Count > 1)
            {
                var best = byStore.OrderBy(s => s.Rate).First();
                insights.Add(new SmartInsightItem
                {
                    Icon = "bi-trophy-fill",
                    Color = "success",
                    Title = $"Best 90-Day Rate: {best.StoreName}",
                    Description = $"Only {best.Rate:F1}% of this store's hires left within 90 days.",
                });
                var worst = byStore.OrderByDescending(s => s.Rate).First();
                if (worst.StoreName != best.StoreName && worst.Rate >= 50)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = "bi-exclamation-triangle-fill",
                        Color = "danger",
                        Title = $"Weakest 90-Day Rate: {worst.StoreName}",
                        Description = $"{worst.Rate:F1}% of this store's hires left within 90 days.",
                    });
            }
        }

        return insights;
    }
}
