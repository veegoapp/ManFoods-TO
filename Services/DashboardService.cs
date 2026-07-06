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

    // Role system simplified to Admin/User — every Admin sees every store now,
    // so this always returns "unrestricted." Kept as a method (instead of
    // deleting every call site) to minimize the blast radius of the change.
    private Task<List<string>?> GetAccessibleStoresAsync(string role, string? assignedName, int? month, int? year) =>
        Task.FromResult<List<string>?>(null);

    // Expands a from/to month-year range (inclusive) into "YYYYMM" sortable int keys.
    internal static List<int> ExpandRangeKeys(int fromMonth, int fromYear, int toMonth, int toYear)
    {
        var start = new DateTime(fromYear, fromMonth, 1);
        var end = new DateTime(toYear, toMonth, 1);
        if (end < start) (start, end) = (end, start);
        var keys = new List<int>();
        for (var d = start; d <= end; d = d.AddMonths(1))
            keys.Add(d.Year * 100 + d.Month);
        return keys;
    }

    // Resolves the explicit set of (month, year) periods a request should aggregate over.
    // When "months" (a CSV of month numbers, e.g. "1,3,5") is given together with "year", those
    // discrete months are used as-is (no requirement that they be contiguous). Otherwise falls
    // back to the legacy contiguous from/to range behavior.
    internal static List<(int Month, int Year)> ResolvePeriods(int? month, int? year, int? fromMonth, int? fromYear, string? months)
    {
        if (year.HasValue && !string.IsNullOrWhiteSpace(months))
        {
            var parsed = months.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var m) ? m : (int?)null)
                .Where(m => m.HasValue && m.Value is >= 1 and <= 12)
                .Select(m => m!.Value)
                .Distinct()
                .OrderBy(m => m)
                .Select(m => (Month: m, Year: year.Value))
                .ToList();
            if (parsed.Count > 0) return parsed;
        }

        var toMonth = month ?? DateTime.Now.Month;
        var toYear  = year  ?? DateTime.Now.Year;
        return ExpandRangeKeys(fromMonth ?? toMonth, fromYear ?? toYear, toMonth, toYear)
            .Select(k => (Month: k % 100, Year: k / 100)).ToList();
    }

    // Stores whose Operation Manager / Operation Consultant (as of the given period) match the filter.
    // Returns null when no OM/OC filter is set (caller should skip the store-list filter entirely).
    private async Task<List<string>?> GetStoresForOmOcAsync(int month, int year, string? om, string? oc)
    {
        if (string.IsNullOrEmpty(om) && string.IsNullOrEmpty(oc)) return null;
        var q = _db.StoreReferences.Where(s => s.Month == month && s.Year == year);
        if (!string.IsNullOrEmpty(om)) q = q.Where(s => s.OperationManager == om);
        if (!string.IsNullOrEmpty(oc)) q = q.Where(s => s.OperationConsultant == oc);
        return await q.Select(s => s.StoreName).Distinct().ToListAsync();
    }

    public async Task<List<string>> GetOperationManagersAsync(int? month, int? year)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        return await q.Where(s => s.OperationManager != "")
            .Select(s => s.OperationManager).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetOperationConsultantsAsync(int? month, int? year)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        return await q.Where(s => s.OperationConsultant != "")
            .Select(s => s.OperationConsultant).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
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
        fromMonth ??= month; fromYear ??= year;

        var cacheKey = $"kpi_{fromMonth}_{fromYear}_{month}_{year}_{store}_{om}_{oc}_{months}_{role}_{assignedName}";
        if (_cache.TryGetValue(cacheKey, out DashboardKpiViewModel? cached) && cached != null)
            return cached;

        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();

        var accessible = await GetAccessibleStoresAsync(role, assignedName, anchor.Month, anchor.Year);
        var omOcStores = await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc);

        var headcountsPerPeriod = new List<int>();
        var toHeadcount = 0;
        var totalResignations = 0;
        var totalNewHires = 0;

        foreach (var p in periods)
        {
            var empQ = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (!string.IsNullOrEmpty(store)) empQ = empQ.Where(e => e.Store == store);
            else if (omOcStores != null) empQ = empQ.Where(e => omOcStores.Contains(e.Store));
            else if (accessible != null && accessible.Count > 0) empQ = empQ.Where(e => accessible.Contains(e.Store));

            var hc = await empQ.CountAsync();
            headcountsPerPeriod.Add(hc);
            if (p.Month == anchor.Month && p.Year == anchor.Year) toHeadcount = hc;

            var resQ = _db.Resignations.Where(r => r.Month == p.Month && r.Year == p.Year);
            if (!string.IsNullOrEmpty(store)) resQ = resQ.Where(r => r.Store == store);
            else if (omOcStores != null) resQ = resQ.Where(r => omOcStores.Contains(r.Store));
            else if (accessible != null && accessible.Count > 0) resQ = resQ.Where(r => accessible.Contains(r.Store));
            totalResignations += await resQ.CountAsync();

            var prevMonth = p.Month == 1 ? 12 : p.Month - 1;
            var prevYear = p.Month == 1 ? p.Year - 1 : p.Year;
            var prevQ = _db.ActiveEmployees.Where(e => e.Month == prevMonth && e.Year == prevYear);
            if (!string.IsNullOrEmpty(store)) prevQ = prevQ.Where(e => e.Store == store);
            else if (omOcStores != null) prevQ = prevQ.Where(e => omOcStores.Contains(e.Store));
            else if (accessible != null && accessible.Count > 0) prevQ = prevQ.Where(e => accessible.Contains(e.Store));

            var prevIds = await prevQ.Select(e => e.EmployeeId).ToListAsync();
            var currIds = await empQ.Select(e => e.EmployeeId).ToListAsync();
            totalNewHires += currIds.Except(prevIds).Count();
        }

        var avgHeadcount = headcountsPerPeriod.Count > 0 ? headcountsPerPeriod.Average() : 0;
        var turnoverRate = avgHeadcount > 0 ? Math.Round(totalResignations / avgHeadcount * 100, 2) : 0;

        var result = new DashboardKpiViewModel
        {
            TotalHeadcount = toHeadcount,
            NewHires = totalNewHires,
            TotalResignations = totalResignations,
            TurnoverRate = turnoverRate,
            Month = anchor.Month,
            Year = anchor.Year
        };

        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    public async Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (!string.IsNullOrEmpty(store)) q = q.Where(r => r.Store == store);
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(r => accessible.Contains(r.Store));

        return await q.GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (!string.IsNullOrEmpty(store)) q = q.Where(r => r.Store == store);
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(r => accessible.Contains(r.Store));

        return await q.Where(r => r.PayrollGroup != "")
            .GroupBy(r => r.PayrollGroup)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (!string.IsNullOrEmpty(store)) q = q.Where(r => r.Store == store);
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(r => accessible.Contains(r.Store));

        var rows = await q.Select(r => new { r.HireDate, r.ResignationDate }).ToListAsync();

        var buckets = new Dictionary<string, int> { ["<3m"] = 0, ["3-6m"] = 0, ["6-12m"] = 0, [">1y"] = 0 };
        foreach (var r in rows)
        {
            if (!r.HireDate.HasValue) { buckets[">1y"]++; continue; }
            var hire = r.HireDate.Value.ToDateTime(TimeOnly.MinValue);
            var resign = r.ResignationDate.HasValue ? r.ResignationDate.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
            var tenureMonths = (resign.Year - hire.Year) * 12 + (resign.Month - hire.Month);
            if (tenureMonths < 3) buckets["<3m"]++;
            else if (tenureMonths < 6) buckets["3-6m"]++;
            else if (tenureMonths < 12) buckets["6-12m"]++;
            else buckets[">1y"]++;
        }

        return buckets.Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }).ToList();
    }

    public async Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.ActiveEmployees.AsQueryable();
        if (month.HasValue) q = q.Where(e => e.Month == month);
        if (year.HasValue) q = q.Where(e => e.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
        else if (month.HasValue && year.HasValue && await GetStoresForOmOcAsync(month.Value, year.Value, om, oc) is { } omOcStores)
            q = q.Where(e => omOcStores.Contains(e.Store));
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

    public async Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();

        var empQ = _db.ActiveEmployees.Where(e => e.Month == anchor.Month && e.Year == anchor.Year);
        if (accessible != null && accessible.Count > 0)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var resQ = _db.Resignations.Where(r => keys.Contains(r.Year * 100 + r.Month));
        if (accessible != null && accessible.Count > 0)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var newHireQ = _db.ActiveEmployees
            .Where(e => e.HireDate != null && keys.Contains(e.HireDate.Value.Year * 100 + e.HireDate.Value.Month));
        if (accessible != null && accessible.Count > 0)
            newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));

        var newHireRaw = await newHireQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var newHiresByStore = newHireRaw.ToDictionary(x => x.Store, x => x.Count);

        // Take first match per store name to avoid duplicate-key issues
        var storeRefList = await _db.StoreReferences
            .Where(s => s.Month == anchor.Month && s.Year == anchor.Year)
            .ToListAsync();
        var storeRefs = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.First());

        var resByStore = resignations.ToDictionary(r => r.Store, r => r.Count);

        var rows = headcounts
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
            });

        if (!string.IsNullOrEmpty(om)) rows = rows.Where(r => r.OperationManager == om);
        if (!string.IsNullOrEmpty(oc)) rows = rows.Where(r => r.OperationConsultant == oc);

        return rows.OrderByDescending(s => s.TurnoverRate).ToList();
    }

    public async Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var stores = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, months);

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

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();
        var current  = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, months);
        if (!current.Any()) return insights;

        // ── Build the equivalent PREVIOUS window (same length as the current selection) ──
        // This ensures comparisons are apples-to-apples regardless of how many months
        // the user has selected (fixes single-month fallback bug).
        var currentPeriods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var periodCount    = currentPeriods.Count;

        int  prevAnchorMonth, prevAnchorYear;
        int? prevFromMonth = null, prevFromYear = null;
        string? prevMonths = null;

        if (!string.IsNullOrWhiteSpace(months))
        {
            // Discrete month selection (e.g. "1,3,5" in 2024) → same months, prior year
            prevAnchorMonth = month;
            prevAnchorYear  = year - 1;
            prevMonths      = months;
        }
        else
        {
            // Contiguous range → shift the entire window back by periodCount months
            var anchorShifted = new DateTime(year, month, 1).AddMonths(-periodCount);
            prevAnchorMonth   = anchorShifted.Month;
            prevAnchorYear    = anchorShifted.Year;

            if (fromMonth.HasValue && fromYear.HasValue)
            {
                var fromShifted = new DateTime(fromYear.Value, fromMonth.Value, 1).AddMonths(-periodCount);
                prevFromMonth   = fromShifted.Month;
                prevFromYear    = fromShifted.Year;
            }
        }

        var previous    = await GetStoreComparisonAsync(prevAnchorMonth, prevAnchorYear, role, assignedName, prevFromMonth, prevFromYear, om, oc, prevMonths);
        var prevByStore = previous.ToDictionary(s => s.StoreName);

        // Human-readable label for comparison window used in descriptions
        var periodLabel = periodCount == 1 ? "last month" : $"prior {periodCount}-month period";

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

        // 3. Spike detection (>= 5% jump vs equivalent prior window)
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
                Description = $"↑ +{delta:F1}% vs {periodLabel} ({prev.TurnoverRate:F1}% → {spike.TurnoverRate:F1}%)."
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
                    ? $"Resignations {(diff > 0 ? "increased" : "decreased")} by {Math.Abs(diff)} vs {periodLabel} ({prevTotal} → {currTotal})."
                    : $"Same number of resignations ({currTotal}) as {periodLabel}."
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

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, int? sinceYear = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);

        // All available periods ordered chronologically
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        if (sinceYear.HasValue)
            periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

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

        // Latest OC/OM assignment per store
        var storeRefList = await _db.StoreReferences.ToListAsync();
        var latestRefByStore = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        var ocByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationConsultant ?? "");
        var omByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationManager ?? "");

        // Build fast lookups
        var hcLookup  = headcounts .ToDictionary(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}", x => x.Count);
        var resLookup = resignations.GroupBy(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}")
                                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var allStores = headcounts.Select(h => h.Store).Distinct().OrderBy(s => s).ToList();
        if (!string.IsNullOrEmpty(om)) allStores = allStores.Where(s => omByStore.TryGetValue(s, out var v) && v == om).ToList();
        if (!string.IsNullOrEmpty(oc)) allStores = allStores.Where(s => ocByStore.TryGetValue(s, out var v) && v == oc).ToList();

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
                OperationConsultant = ocByStore.TryGetValue(store, out var ocVal) ? ocVal : "",
                OperationManager    = omByStore.TryGetValue(store, out var omVal) ? omVal : "",
                PeriodRates         = periodRates,
                AvgRate             = nonNullRates.Count > 0
                                        ? Math.Round(nonNullRates.Average(), 1)
                                        : null
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }

    // ── Active-workforce composition (Workforce page) ──────────────────────
    // Snapshots of who currently works here, as opposed to the Turnover-page
    // methods above which describe who resigned.

    public async Task<List<ChartDataItem>> GetHeadcountByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.ActiveEmployees.AsQueryable();
        if (month.HasValue) q = q.Where(e => e.Month == month);
        if (year.HasValue) q = q.Where(e => e.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
        else if (month.HasValue && year.HasValue && await GetStoresForOmOcAsync(month.Value, year.Value, om, oc) is { } omOcStores)
            q = q.Where(e => omOcStores.Contains(e.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(e => accessible.Contains(e.Store));

        return await q.GroupBy(e => e.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetHeadcountByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.ActiveEmployees.AsQueryable();
        if (month.HasValue) q = q.Where(e => e.Month == month);
        if (year.HasValue) q = q.Where(e => e.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
        else if (month.HasValue && year.HasValue && await GetStoresForOmOcAsync(month.Value, year.Value, om, oc) is { } omOcStores)
            q = q.Where(e => omOcStores.Contains(e.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(e => accessible.Contains(e.Store));

        return await q.Where(e => e.PayrollGroup != "")
            .GroupBy(e => e.PayrollGroup)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetHeadcountByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.ActiveEmployees.Where(e => e.HireDate != null);
        if (month.HasValue) q = q.Where(e => e.Month == month);
        if (year.HasValue) q = q.Where(e => e.Year == year);
        if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
        else if (month.HasValue && year.HasValue && await GetStoresForOmOcAsync(month.Value, year.Value, om, oc) is { } omOcStores)
            q = q.Where(e => omOcStores.Contains(e.Store));
        else if (accessible != null && accessible.Count > 0) q = q.Where(e => accessible.Contains(e.Store));

        var hireDates = await q.Select(e => e.HireDate!.Value).ToListAsync();
        if (hireDates.Count == 0) return new List<ChartDataItem>();

        var asOfYear  = year ?? DateTime.Now.Year;
        var asOfMonth = month ?? DateTime.Now.Month;
        var asOf = new DateOnly(asOfYear, asOfMonth, DateTime.DaysInMonth(asOfYear, asOfMonth));

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
                Value = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max)
            })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetHeadcountTrendAsync(string? store, string? om, string? oc, int? sinceYear)
    {
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();
        if (sinceYear.HasValue) periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

        var result = new List<ChartDataItem>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (!string.IsNullOrEmpty(store)) q = q.Where(e => e.Store == store);
            else if (await GetStoresForOmOcAsync(p.Month, p.Year, om, oc) is { } omOcStores) q = q.Where(e => omOcStores.Contains(e.Store));

            var count = await q.CountAsync();
            result.Add(new ChartDataItem { Label = $"{p.Year:D4}-{p.Month:D2}", Value = count });
        }
        return result;
    }

    public async Task<List<StoreHeadcountRow>> GetStoreHeadcountBreakdownAsync(int month, int year, string? om, string? oc)
    {
        var omOcStores = await GetStoresForOmOcAsync(month, year, om, oc);
        var q = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));

        var rows = await q.Select(e => new { e.Store, e.Gender }).ToListAsync();
        return rows.GroupBy(r => r.Store)
            .Select(g => new StoreHeadcountRow
            {
                StoreName       = g.Key,
                Headcount       = g.Count(),
                GenderBreakdown = g.GroupBy(x => x.Gender).ToDictionary(gg => gg.Key, gg => gg.Count())
            })
            .OrderByDescending(r => r.Headcount)
            .ToList();
    }
}
