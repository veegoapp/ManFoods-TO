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

    public async Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var keys = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months)
            .Select(p => p.Year * 100 + p.Month).ToHashSet();
        var omOcStores = await GetStoresForOmOcAsync(om, oc);
        var stores = MultiValueFilter.Split(store);
        var earlyLeaverIds = resTenures
            .Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && r.TenureDays <= 90
                     && (stores == null || stores.Contains(r.Store))
                     && (omOcStores == null || omOcStores.Contains(r.Store)))
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
}
