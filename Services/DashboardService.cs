using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardService(AppDbContext db, IMemoryCache cache) { _db = db; _cache = cache; }

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
        var cacheKey = $"kpi_{month}_{year}_{store}_{role}_{assignedName}";
        if (_cache.TryGetValue(cacheKey, out DashboardKpiViewModel? cached) && cached != null)
            return cached;

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

        var result = new DashboardKpiViewModel
        {
            TotalHeadcount = headcount,
            NewHires = newHires,
            TotalResignations = resignations,
            TurnoverRate = turnoverRate,
            Month = month.Value,
            Year = year.Value
        };

        _cache.Set(cacheKey, result, CacheDuration);
        return result;
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

    public async Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);

        var empQ = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null && accessible.Count > 0)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var resQ = _db.Resignations.Where(r => r.Month == month && r.Year == year);
        if (accessible != null && accessible.Count > 0)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var newHireRaw = await empQ
            .Where(e => e.HireDate != null
                     && e.HireDate.Value.Month == month
                     && e.HireDate.Value.Year  == year)
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var newHiresByStore = newHireRaw.ToDictionary(x => x.Store, x => x.Count);

        // Take first match per store name to avoid duplicate-key issues
        var storeRefList = await _db.StoreReferences
            .Where(s => s.Month == month && s.Year == year)
            .ToListAsync();
        var storeRefs = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.First());

        var resByStore = resignations.ToDictionary(r => r.Store, r => r.Count);

        return headcounts
            .Select(h =>
            {
                var res = resByStore.TryGetValue(h.Store, out var r) ? r : 0;
                var nh  = newHiresByStore.TryGetValue(h.Store, out var n) ? n : 0;
                storeRefs.TryGetValue(h.Store, out var sr);
                return new StoreComparisonRow
                {
                    StoreName           = h.Store,
                    Headcount           = h.Count,
                    NewHires            = nh,
                    Resignations        = res,
                    TurnoverRate        = h.Count > 0 ? Math.Round((double)res / h.Count * 100, 1) : 0,
                    OperationConsultant = sr?.OperationConsultant ?? "",
                    OperationManager    = sr?.OperationManager    ?? ""
                };
            })
            .OrderByDescending(s => s.TurnoverRate)
            .ToList();
    }

    public async Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName)
    {
        var stores = await GetStoreComparisonAsync(month, year, role, assignedName);

        OcOmRow ToRow(IGrouping<string, StoreComparisonRow> g, string type) => new()
        {
            Name              = g.Key,
            Type              = type,
            StoreCount        = g.Count(),
            TotalResignations = g.Sum(s => s.Resignations),
            TotalHeadcount    = g.Sum(s => s.Headcount),
            AvgTurnoverRate   = g.Sum(s => s.Headcount) > 0
                ? Math.Round((double)g.Sum(s => s.Resignations) / g.Sum(s => s.Headcount) * 100, 1)
                : 0
        };

        var ocRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => ToRow(g, "OC"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        var omRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => ToRow(g, "OM"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        return new OcOmAnalysisResult { OcRows = ocRows, OmRows = omRows };
    }

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName)
    {
        var insights = new List<SmartInsightItem>();
        var current  = await GetStoreComparisonAsync(month, year, role, assignedName);
        if (!current.Any()) return insights;

        var prevMonth   = month == 1 ? 12 : month - 1;
        var prevYear    = month == 1 ? year - 1 : year;
        var previous    = await GetStoreComparisonAsync(prevMonth, prevYear, role, assignedName);
        var prevByStore = previous.ToDictionary(s => s.StoreName);

        // 1. Highest turnover store
        var highest = current.First();
        if (highest.TurnoverRate > 0)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-exclamation-triangle-fill",
                Color       = "danger",
                Title       = $"Highest Turnover: {highest.StoreName}",
                Description = $"{highest.TurnoverRate:F1}% turnover — {highest.Resignations} resignation(s) from {highest.Headcount} employees."
            });

        // 2. Best performing store
        var best = current.Where(s => s.Headcount > 0).OrderBy(s => s.TurnoverRate).FirstOrDefault();
        if (best != null && current.Count > 1 && best.StoreName != highest.StoreName)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-check-circle-fill",
                Color       = "success",
                Title       = $"Best Performing: {best.StoreName}",
                Description = $"Lowest turnover at {best.TurnoverRate:F1}% with only {best.Resignations} resignation(s)."
            });

        // 3. Spike detection (>= 5% jump vs previous month)
        var spikes = current
            .Where(s => prevByStore.TryGetValue(s.StoreName, out var p) && s.TurnoverRate - p.TurnoverRate >= 5)
            .OrderByDescending(s => s.TurnoverRate - prevByStore[s.StoreName].TurnoverRate)
            .Take(3)
            .ToList();

        foreach (var spike in spikes)
        {
            var prev  = prevByStore[spike.StoreName];
            var delta = spike.TurnoverRate - prev.TurnoverRate;
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-graph-up-arrow",
                Color       = "warning",
                Title       = $"Turnover Spike: {spike.StoreName}",
                Description = $"↑ +{delta:F1}% from last month ({prev.TurnoverRate:F1}% → {spike.TurnoverRate:F1}%)."
            });
        }

        // 4. Overall trend
        if (previous.Any())
        {
            var currTotal = current.Sum(s => s.Resignations);
            var prevTotal = previous.Sum(s => s.Resignations);
            var diff      = currTotal - prevTotal;
            insights.Add(new SmartInsightItem
            {
                Icon        = diff > 0 ? "bi-arrow-up-circle-fill" : diff < 0 ? "bi-arrow-down-circle-fill" : "bi-dash-circle-fill",
                Color       = diff > 0 ? "danger" : diff < 0 ? "success" : "secondary",
                Title       = diff > 0 ? "Trend: Worsening" : diff < 0 ? "Trend: Improving" : "Trend: Stable",
                Description = diff != 0
                    ? $"Resignations {(diff > 0 ? "increased" : "decreased")} by {Math.Abs(diff)} vs last month ({prevTotal} → {currTotal})."
                    : $"Same number of resignations ({currTotal}) as last month."
            });
        }

        // 5. Worst OC by weighted turnover rate
        var worstOc = current
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => new
            {
                Name            = g.Key,
                StoreCount      = g.Count(),
                TotalRes        = g.Sum(s => s.Resignations),
                TotalHead       = g.Sum(s => s.Headcount),
                AvgTurnoverRate = g.Sum(s => s.Headcount) > 0
                    ? Math.Round((double)g.Sum(s => s.Resignations) / g.Sum(s => s.Headcount) * 100, 1) : 0
            })
            .OrderByDescending(g => g.AvgTurnoverRate)
            .FirstOrDefault();

        if (worstOc != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-fill-exclamation",
                Color       = "warning",
                Title       = $"Highest OC Turnover: {worstOc.Name}",
                Description = $"{worstOc.AvgTurnoverRate:F1}% weighted avg across {worstOc.StoreCount} store(s) — {worstOc.TotalRes} total resignation(s)."
            });

        // 6. Worst OM by weighted turnover rate
        var worstOm = current
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => new
            {
                Name            = g.Key,
                StoreCount      = g.Count(),
                TotalRes        = g.Sum(s => s.Resignations),
                TotalHead       = g.Sum(s => s.Headcount),
                AvgTurnoverRate = g.Sum(s => s.Headcount) > 0
                    ? Math.Round((double)g.Sum(s => s.Resignations) / g.Sum(s => s.Headcount) * 100, 1) : 0
            })
            .OrderByDescending(g => g.AvgTurnoverRate)
            .FirstOrDefault();

        if (worstOm != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-badge-fill",
                Color       = "warning",
                Title       = $"Highest OM Turnover: {worstOm.Name}",
                Description = $"{worstOm.AvgTurnoverRate:F1}% weighted avg across {worstOm.StoreCount} store(s) — {worstOm.TotalRes} total resignation(s)."
            });

        return insights;
    }

    public async Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);

        // Headcount per store
        var empQ = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null && accessible.Count > 0)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // Resignations per store
        var resQ = _db.Resignations.Where(r => r.Month == month && r.Year == year);
        if (accessible != null && accessible.Count > 0)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // Previous month for new hires
        var prevMonth = month == 1 ? 12 : month - 1;
        var prevYear  = month == 1 ? year - 1 : year;

        var prevQ = _db.ActiveEmployees.Where(e => e.Month == prevMonth && e.Year == prevYear);
        if (accessible != null && accessible.Count > 0)
            prevQ = prevQ.Where(e => accessible.Contains(e.Store));

        var prevIds = await prevQ.Select(e => new { e.Store, e.EmployeeId }).ToListAsync();
        var currIds = await empQ.Select(e => new { e.Store, e.EmployeeId }).ToListAsync();

        var prevByStore = prevIds.GroupBy(x => x.Store)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EmployeeId).ToHashSet());

        var newHiresByStore = currIds
            .GroupBy(x => x.Store)
            .ToDictionary(
                g => g.Key,
                g => g.Count(x => !prevByStore.TryGetValue(x.Store, out var ids) || !ids.Contains(x.EmployeeId)));

        var resByStore = resignations.ToDictionary(r => r.Store, r => r.Count);

        return headcounts
            .Select(h =>
            {
                var res = resByStore.TryGetValue(h.Store, out var r) ? r : 0;
                var nh  = newHiresByStore.TryGetValue(h.Store, out var n) ? n : 0;
                return new StoreBreakdown
                {
                    Store       = h.Store,
                    Headcount   = h.Count,
                    Resignations = res,
                    TurnoverRate = h.Count > 0 ? Math.Round((double)res / h.Count * 100, 1) : 0,
                    NewHires    = nh
                };
            })
            .OrderByDescending(s => s.TurnoverRate)
            .ToList();
    }

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);

        // All available periods ordered chronologically
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        var periodKeys = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        // Headcounts grouped by store + period
        var empQ = _db.ActiveEmployees.AsQueryable();
        if (accessible != null && accessible.Count > 0)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Resignations grouped by store + period
        var resQ = _db.Resignations.AsQueryable();
        if (accessible != null && accessible.Count > 0)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Latest OC assignment per store
        var storeRefList = await _db.StoreReferences.ToListAsync();
        var ocByStore = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
                      .First().OperationConsultant ?? "");

        // Build fast lookups
        var hcLookup  = headcounts .ToDictionary(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}", x => x.Count);
        var resLookup = resignations.GroupBy(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}")
                                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var allStores = headcounts.Select(h => h.Store).Distinct().OrderBy(s => s).ToList();

        var rows = allStores.Select(store =>
        {
            var periodRates = new Dictionary<string, double?>();
            var nonNullRates = new List<double>();

            foreach (var pk in periodKeys)
            {
                var key = $"{store}|{pk}";
                if (hcLookup.TryGetValue(key, out var hc) && hc > 0)
                {
                    var res  = resLookup.TryGetValue(key, out var rc) ? rc : 0;
                    var rate = Math.Round((double)res / hc * 100, 1);
                    periodRates[pk] = rate;
                    nonNullRates.Add(rate);
                }
                else
                {
                    periodRates[pk] = null;
                }
            }

            return new TrendMatrixRow
            {
                StoreName           = store,
                OperationConsultant = ocByStore.TryGetValue(store, out var oc) ? oc : "",
                PeriodRates         = periodRates,
                AvgRate             = nonNullRates.Count > 0
                                        ? Math.Round(nonNullRates.Average(), 1)
                                        : null
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }
}
