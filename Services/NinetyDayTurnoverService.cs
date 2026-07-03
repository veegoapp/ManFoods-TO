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
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();
        return rows.Select(r => (r.EmployeeId, r.Store, r.HireDate!.Value.Month, r.HireDate!.Value.Year)).ToList();
    }

    private async Task<List<ResignationTenure>> LoadResignationTenuresAsync()
    {
        var rows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
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

    private static NinetyDayKpiViewModel ComputeKpi(
        List<(string EmployeeId, string Store, int Month, int Year)> activeHires,
        List<ResignationTenure> resTenures,
        int month, int year, string? store)
    {
        var fromActive = activeHires.Where(a => a.Month == month && a.Year == year && (store == null || a.Store == store)).Select(a => a.EmployeeId);
        var fromRes = resTenures.Where(r => r.HireDate.Month == month && r.HireDate.Year == year && (store == null || r.Store == store)).Select(r => r.EmployeeId);
        var hireIds = fromActive.Concat(fromRes).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToHashSet();

        var earlyLeaverIds = resTenures
            .Where(r => r.HireDate.Month == month && r.HireDate.Year == year && r.TenureDays <= 90 && (store == null || r.Store == store))
            .Select(r => r.EmployeeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToHashSet();

        var totalHires = hireIds.Count;
        var earlyLeavers = earlyLeaverIds.Count;
        var rate = totalHires > 0 ? Math.Round(earlyLeavers * 100.0 / totalHires, 1) : 0;

        var cohortCloseDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var isProvisional = DateOnly.FromDateTime(DateTime.UtcNow) < cohortCloseDate.AddDays(90);

        return new NinetyDayKpiViewModel
        {
            CohortMonth = month,
            CohortYear = year,
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

    public async Task<NinetyDayKpiViewModel> GetKpiAsync(int month, int year, string? store)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        return ComputeKpi(activeHires, resTenures, month, year, store);
    }

    public async Task<List<RateTrendItem>> GetTrendAsync(string? store)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();

        var periods = activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();

        var result = new List<RateTrendItem>();
        foreach (var (m, y) in periods)
        {
            var kpi = ComputeKpi(activeHires, resTenures, m, y, store);
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

    public async Task<List<ChartDataItem>> GetByStoreAsync(int month, int year)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();

        var stores = activeHires.Where(a => a.Month == month && a.Year == year).Select(a => a.Store)
            .Concat(resTenures.Where(r => r.HireDate.Month == month && r.HireDate.Year == year).Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();

        var result = new List<ChartDataItem>();
        foreach (var store in stores)
        {
            var kpi = ComputeKpi(activeHires, resTenures, month, year, store);
            if (kpi.TotalHires == 0) continue;
            result.Add(new ChartDataItem { Label = store, Value = (int)Math.Round(kpi.Rate) });
        }
        return result.OrderByDescending(c => c.Value).ToList();
    }

    public async Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store)
    {
        var resTenures = await LoadResignationTenuresAsync();
        return resTenures
            .Where(r => r.HireDate.Month == month && r.HireDate.Year == year && r.TenureDays <= 90 && (store == null || r.Store == store))
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

    public async Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var earlyLeaverIds = resTenures
            .Where(r => r.HireDate.Month == month && r.HireDate.Year == year && r.TenureDays <= 90 && (store == null || r.Store == store))
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
