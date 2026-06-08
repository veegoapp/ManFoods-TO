using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    private async Task<List<string>?> GetAccessibleStoresAsync(string role, string? assignedName, int? month, int? year)
    {
        if (role == "Admin_Full" || role == "Admin_Read") return null;

        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month.Value);
        if (year.HasValue) q = q.Where(s => s.Year == year.Value);

        if (role == "Operation_Manager" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(s => s.OperationManager == assignedName);
        else if (role == "Operation_Consultant" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(s => s.OperationConsultant == assignedName);

        return await q.Select(s => s.StoreName).ToListAsync();
    }

    public async Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName)
    {
        if (!month.HasValue || !year.HasValue)
        {
            var latest = await _db.ActiveEmployees
                .OrderByDescending(e => e.Year).ThenByDescending(e => e.Month)
                .Select(e => new { e.Month, e.Year })
                .FirstOrDefaultAsync();
            month ??= latest?.Month ?? DateTime.Now.Month;
            year ??= latest?.Year ?? DateTime.Now.Year;
        }

        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);

        var empQ = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (!string.IsNullOrEmpty(store)) empQ = empQ.Where(e => e.Store == store);
        else if (accessible != null && accessible.Count > 0) empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcount = await empQ.CountAsync();

        var resQ = _db.Resignations.Where(r => r.Month == month && r.Year == year);
        if (!string.IsNullOrEmpty(store)) resQ = resQ.Where(r => r.Store == store);
        else if (accessible != null && accessible.Count > 0) resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ.CountAsync();

        var prevMonth = month == 1 ? 12 : month.Value - 1;
        var prevYear = month == 1 ? year.Value - 1 : year.Value;

        var prevQ = _db.ActiveEmployees.Where(e => e.Month == prevMonth && e.Year == prevYear);
        if (!string.IsNullOrEmpty(store)) prevQ = prevQ.Where(e => e.Store == store);
        else if (accessible != null && accessible.Count > 0) prevQ = prevQ.Where(e => accessible.Contains(e.Store));

        var prevIds = await prevQ.Select(e => e.EmployeeId).ToListAsync();
        var currIds = await empQ.Select(e => e.EmployeeId).ToListAsync();
        var newHires = currIds.Except(prevIds).Count();

        var turnoverRate = headcount > 0 ? Math.Round((double)resignations / headcount * 100, 2) : 0;

        return new DashboardKpiViewModel
        {
            TotalHeadcount = headcount,
            NewHires = newHires,
            TotalResignations = resignations,
            TurnoverRate = turnoverRate,
            Month = month.Value,
            Year = year.Value
        };
    }

    public async Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        if (month.HasValue) q = q.Where(r => r.Month == month);
        if (year.HasValue) q = q.Where(r => r.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(r => r.Store == store);
        else if (accessible != null && accessible.Count > 0) q = q.Where(r => accessible.Contains(r.Store));

        return await q.GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        if (month.HasValue) q = q.Where(r => r.Month == month);
        if (year.HasValue) q = q.Where(r => r.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(r => r.Store == store);
        else if (accessible != null && accessible.Count > 0) q = q.Where(r => accessible.Contains(r.Store));

        var rows = await q.Select(r => new { r.HireDate, r.ResignationDate }).ToListAsync();

        var buckets = new Dictionary<string, int> { ["<3m"] = 0, ["3-6m"] = 0, ["6-12m"] = 0, [">1y"] = 0 };
        foreach (var r in rows)
        {
            if (!r.HireDate.HasValue) { buckets[">1y"]++; continue; }
            var hire = r.HireDate.Value.ToDateTime(TimeOnly.MinValue);
            var resign = r.ResignationDate.HasValue ? r.ResignationDate.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
            var months = (resign.Year - hire.Year) * 12 + (resign.Month - hire.Month);
            if (months < 3) buckets["<3m"]++;
            else if (months < 6) buckets["3-6m"]++;
            else if (months < 12) buckets["6-12m"]++;
            else buckets[">1y"]++;
        }

        return buckets.Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }).ToList();
    }

    public async Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.ActiveEmployees.AsQueryable();
        if (month.HasValue) q = q.Where(e => e.Month == month);
        if (year.HasValue) q = q.Where(e => e.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
        else if (accessible != null && accessible.Count > 0) q = q.Where(e => accessible.Contains(e.Store));

        return await q.GroupBy(e => e.Gender)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .ToListAsync();
    }

    public async Task<List<PeriodItem>> GetAvailablePeriodsAsync()
    {
        return await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodItem { Month = p.Month, Year = p.Year })
            .ToListAsync();
    }
}
