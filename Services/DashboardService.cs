using System.Linq.Expressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;

namespace MvcApp.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IStoreAccessService _storeAccess;
    private readonly IStringLocalizer<SharedResource> _L;
    // Results keyed by filter values (KPIs, store comparison) — bounded, see FilterResultCache.
    private readonly FilterResultCache _filterCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardService(AppDbContext db, IMemoryCache cache, IStoreAccessService storeAccess, IStringLocalizer<SharedResource> localizer, FilterResultCache? filterCache = null)
    {
        _db = db;
        _cache = cache;
        _filterCache = filterCache ?? new FilterResultCache();
        _storeAccess = storeAccess;
        _L = localizer;
    }

    // Returns the store names a restricted role (Operation Manager/Consultant,
    // Head Manager, ...) is limited to, or null for unrestricted access
    // (Admin/User). "assignedName" here is actually the logged-in user's email
    // (see HttpContext.Session.GetEmail() at call sites). Delegates to
    // IStoreAccessService, the single source of truth — always resolved
    // against the latest uploaded period regardless of which month/year this
    // particular call is displaying, so callers keep passing month/year
    // unchanged (kept for signature compatibility with ~18 call sites) even
    // though the access check itself no longer scopes by them.
    private Task<List<string>?> GetAccessibleStoresAsync(string role, string? assignedName, int? month, int? year) =>
        _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);

    // Expands a from/to month-year range (inclusive) into "YYYYMM" sortable int keys.
    internal static List<int> ExpandRangeKeys(int fromMonth, int fromYear, int toMonth, int toYear)
    {
        // Defence in depth behind ReportParameterValidationFilter: never build an unbounded key list
        // (it becomes a SQL "IN (...)" list) or let new DateTime(...) throw for a bad month/year.
        if (!PeriodLimits.IsValidMonth(fromMonth) || !PeriodLimits.IsValidMonth(toMonth) ||
            !PeriodLimits.IsValidYear(fromYear) || !PeriodLimits.IsValidYear(toYear))
            throw new ArgumentOutOfRangeException(nameof(fromMonth), "Month must be 1-12 and year within the supported range.");
        if (PeriodLimits.SpanInMonths(fromMonth, fromYear, toMonth, toYear) > PeriodLimits.MaxRangeMonths)
            throw new ArgumentOutOfRangeException(nameof(fromYear), $"The requested period range is longer than {PeriodLimits.MaxRangeMonths} months.");

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

        // A year-only selection (no specific month, no explicit months list, no
        // from-range) means "the whole year" to every year+months-style caller
        // (Scorecard, Early Warning, Exit Interviews) — falling through to the
        // month-snapshot default below silently narrowed it to whatever the
        // server's wall-clock month happens to be right now, which is usually
        // empty for the selected year and made those reports look broken.
        if (year.HasValue && !month.HasValue && !fromMonth.HasValue && !fromYear.HasValue)
        {
            return Enumerable.Range(1, 12).Select(m => (Month: m, Year: year.Value)).ToList();
        }

        var toMonth = month ?? DateTime.Now.Month;
        var toYear  = year  ?? DateTime.Now.Year;
        return ExpandRangeKeys(fromMonth ?? toMonth, fromYear ?? toYear, toMonth, toYear)
            .Select(k => (Month: k % 100, Year: k / 100)).ToList();
    }

    // New Hires: an ActiveEmployees row counts as a new hire for its OWN (Month, Year)
    // snapshot only when that same row's HireDate falls in that exact month — strictly
    // HireDate-driven, never a roster diff against the prior month's active list. Scoping
    // to the row's own snapshot period (rather than just "HireDate falls somewhere in the
    // selected range") also prevents the same employee being counted once per monthly
    // snapshot they still appear in when a multi-month range is selected.
    private IQueryable<ActiveEmployee> NewHiresQuery(IEnumerable<int> periodKeys)
    {
        var keys = periodKeys as ICollection<int> ?? periodKeys.ToList();
        return _db.ActiveEmployees.Where(e => e.HireDate != null
            && keys.Contains(e.Year * 100 + e.Month)
            && (e.HireDate!.Value.Year * 100 + e.HireDate!.Value.Month) == (e.Year * 100 + e.Month));
    }

    // Stores whose Operation Manager / Operation Consultant (as of the given period) match the filter.
    // Returns null when no OM/OC filter is set (caller should skip the store-list filter entirely).
    // om/oc/soc/od are comma-separated multi-select values (see MultiValueFilter) — a plain "==" equality
    // check here would silently match zero stores whenever more than one value was selected upstream
    // (Reports/dashboard filter panels always send a CSV, even for a single choice).
    private async Task<List<string>?> GetStoresForOmOcAsync(int month, int year, string? om, string? oc, string? soc = null, string? od = null)
    {
        var oms = MultiValueFilter.Split(om);
        var ocs = MultiValueFilter.Split(oc);
        var socs = MultiValueFilter.Split(soc);
        var ods = MultiValueFilter.Split(od);
        if (oms == null && ocs == null && socs == null && ods == null) return null;
        var q = _db.StoreReferences.Where(s => s.Month == month && s.Year == year);
        if (oms != null) q = q.Where(s => oms.Contains(s.OperationManager));
        if (ocs != null) q = q.Where(s => ocs.Contains(s.OperationConsultant));
        if (socs != null) q = q.Where(s => socs.Contains(s.SeniorOperationConsultant));
        if (ods != null) q = q.Where(s => ods.Contains(s.OperationDirector));
        return await q.Select(s => s.StoreName).Distinct().ToListAsync();
    }

    // Fetches only the row(s) that could be "the latest period" for each store —
    // i.e. rows whose (Year, Month) matches that store's own max — instead of
    // pulling every historical period for every store over the wire. Ties (more
    // than one row sharing a store's max period) are resolved by the caller with
    // the exact same OrderByDescending(Year).ThenByDescending(Month).First() rule
    // used before this optimization, so the result is identical either way.
    private async Task<List<StoreReference>> LoadLatestStoreReferenceCandidatesAsync() =>
        await _db.StoreReferences
            .Where(s => s.Year * 100 + s.Month == _db.StoreReferences
                .Where(x => x.StoreName == s.StoreName)
                .Max(x => x.Year * 100 + x.Month))
            .ToListAsync();

    public async Task<List<string>> GetOperationManagersAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        // Restricted roles must never see OM names outside their own accessible
        // stores in the filter dropdown, regardless of month/year selected.
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationManager != "")
            .Select(s => s.OperationManager).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetOperationConsultantsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationConsultant != "")
            .Select(s => s.OperationConsultant).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetSeniorOperationConsultantsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.SeniorOperationConsultant != "")
            .Select(s => s.SeniorOperationConsultant).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetOperationDirectorsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationDirector != "")
            .Select(s => s.OperationDirector).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetJobTitlesAsync(int? month, int? year, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var active = _db.ActiveEmployees.AsQueryable();
        var resignations = _db.Resignations.AsQueryable();
        if (month.HasValue) {
            active = active.Where(e => e.Month == month);
            resignations = resignations.Where(e => e.Month == month);
        }
        if (year.HasValue) {
            active = active.Where(e => e.Year == year);
            resignations = resignations.Where(e => e.Year == year);
        }
        if (accessible != null) {
            active = active.Where(e => accessible.Contains(e.Store));
            resignations = resignations.Where(e => accessible.Contains(e.Store));
        }
        var activeJobs = await active.Where(e => e.JobTitle != "").Select(e => e.JobTitle).ToListAsync();
        var resignedJobs = await resignations.Where(e => e.JobTitle != "").Select(e => e.JobTitle).ToListAsync();
        return activeJobs.Concat(resignedJobs).Distinct().OrderBy(s => s).ToList();
    }

    public async Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
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

        var cacheKey = FilterResultCache.BuildKey("kpi", fromMonth, fromYear, month, year,
            FilterResultCache.NormalizeList(store), FilterResultCache.NormalizeList(om), FilterResultCache.NormalizeList(oc),
            FilterResultCache.NormalizeList(soc), FilterResultCache.NormalizeList(od), FilterResultCache.NormalizeMonths(months),
            FilterResultCache.NormalizeList(jobTitles), role, assignedName);
        if (_filterCache.TryGet(cacheKey, out DashboardKpiViewModel? cached))
            return cached!;

        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();

        var accessible = await GetAccessibleStoresAsync(role, assignedName, anchor.Month, anchor.Year);
        var omOcStores = await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od);
        var stores = MultiValueFilter.Split(store);
        var jobs = MultiValueFilter.Split(jobTitles);

        var headcountsPerPeriod = new List<int>();
        var totalResignations = 0;

        foreach (var p in periods)
        {
            // Role-based store access is ALWAYS applied (never bypassed by an
            // explicit store/om/oc selection) — the explicit filter, when present,
            // narrows further on top of it. Final population = accessible ∩ explicit.
            var empQ = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) empQ = empQ.Where(e => accessible.Contains(e.Store));
            if (stores != null) empQ = empQ.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) empQ = empQ.Where(e => omOcStores.Contains(e.Store));
            if (jobs != null) empQ = empQ.Where(e => jobs.Contains(e.JobTitle));

            var hc = await empQ.CountAsync();
            headcountsPerPeriod.Add(hc);

            var resQ = _db.Resignations.Where(r => r.Month == p.Month && r.Year == p.Year);
            if (accessible != null) resQ = resQ.Where(r => accessible.Contains(r.Store));
            if (stores != null) resQ = resQ.Where(r => stores.Contains(r.Store));
            else if (omOcStores != null) resQ = resQ.Where(r => omOcStores.Contains(r.Store));
            if (jobs != null) resQ = resQ.Where(r => jobs.Contains(r.JobTitle));
            totalResignations += await resQ.CountAsync();
        }

        // New Hires: strictly HireDate-driven across the resolved periods — no
        // roster-diffing against the prior month's active list.
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToList();
        var newHireQ = NewHiresQuery(periodKeys);
        if (accessible != null) newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));
        if (stores != null) newHireQ = newHireQ.Where(e => stores.Contains(e.Store));
        else if (omOcStores != null) newHireQ = newHireQ.Where(e => omOcStores.Contains(e.Store));
        if (jobs != null) newHireQ = newHireQ.Where(e => jobs.Contains(e.JobTitle));
        var totalNewHires = await newHireQ.CountAsync();

        // Turnover Rate still divides by the AVERAGE headcount across the range
        // (a rate needs a fair per-period denominator) — only the displayed
        // Total Headcount below changes to a sum when a period range is selected.
        var avgHeadcount = headcountsPerPeriod.Count > 0 ? headcountsPerPeriod.Average() : 0;
        var turnoverRate = MetricsCalculationService.RatePercent(totalResignations, avgHeadcount, 2);

        var result = new DashboardKpiViewModel
        {
            // Selecting a From→To period range adds each period's headcount
            // together (e.g. Jan + Feb) rather than showing only the latest
            // period's snapshot — the same "range = sum" behavior as New Hires/
            // Resignations below, applied consistently across every dashboard
            // page that shows this card.
            TotalHeadcount = headcountsPerPeriod.Sum(),
            NewHires = totalNewHires,
            TotalResignations = totalResignations,
            TurnoverRate = turnoverRate,
            Month = anchor.Month,
            Year = anchor.Year
        };

        _filterCache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    public async Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
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
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(r => jobs.Contains(r.JobTitle));

        return await q.GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
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
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(r => jobs.Contains(r.JobTitle));

        var rows = await q.Where(r => r.PayrollGroup != "")
            .GroupBy(r => r.PayrollGroup)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        return rows
            .Select(x => new ChartDataItem { Label = DataLabelTranslator.PayrollGroup(x.Key, _L), Value = x.Count })
            .OrderByDescending(x => x.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
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
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(r => jobs.Contains(r.JobTitle));

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

    // Averages a per-category active-employee count across the resolved periods,
    // instead of a single snapshot month — so a multi-month range on Turnover/
    // Workforce doesn't silently collapse the Gender/Headcount composition
    // charts down to the latest month while every other chart on the page
    // honors the full range. Averaging (rather than summing) avoids double-
    // counting the same still-employed person across multiple monthly snapshots.
    // Range = sum: selecting a From→To range adds each period's counts
    // together (e.g. Jan + Feb headcount by gender), matching GetKpisAsync's
    // Total Headcount/New Hires/Resignations behavior above.
    private async Task<List<ChartDataItem>> SumBreakdownAsync(
        List<(int Month, int Year)> periods, List<string>? accessible, List<string>? stores, List<string>? omOcStores, List<string>? jobs,
        Expression<Func<ActiveEmployee, string>> keySelector, bool excludeEmptyKey)
    {
        var totals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));
            if (jobs != null) q = q.Where(e => jobs.Contains(e.JobTitle));

            // Group and count server-side instead of pulling every matching
            // ActiveEmployee row (all columns) over the wire just to group by
            // one field in C# — same result, far less data transferred per
            // period (was the single biggest source of slow page loads once
            // Neon was swapped for a remote SQL Server with less network
            // headroom: this one query alone was taking 6+ seconds).
            var grouped = await q.GroupBy(keySelector)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var group in grouped)
            {
                if (excludeEmptyKey && string.IsNullOrEmpty(group.Key)) continue;
                totals[group.Key] = totals.GetValueOrDefault(group.Key) + group.Count;
            }
        }

        return totals
            .Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value })
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.AsQueryable();
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            if (MultiValueFilter.Split(jobTitles) is { } jobs) qAll = qAll.Where(e => jobs.Contains(e.JobTitle));
            var rowsAll = await qAll.GroupBy(e => e.Gender)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();
            return rowsAll
                .Select(x => new ChartDataItem { Label = DataLabelTranslator.Gender(x.Key, _L), Value = x.Count })
                .OrderBy(c => c.Label)
                .ToList();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        var summed = await SumBreakdownAsync(periods, accessible, stores, omOcStores, MultiValueFilter.Split(jobTitles), e => e.Gender, excludeEmptyKey: false);
        return summed
            .Select(c => new ChartDataItem { Label = DataLabelTranslator.Gender(c.Label, _L), Value = c.Value })
            .OrderBy(c => c.Label)
            .ToList();
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
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        // Multiple sibling AJAX endpoints (store-comparison, oc-om-analysis,
        // smart-insights) each need this exact result for the same request
        // parameters — cache it the same way GetKpisAsync is cached, so a single
        // page load computes it once instead of recomputing it 2-4x.
        var cacheKey = FilterResultCache.BuildKey("store-comparison", month, year, fromMonth, fromYear,
            FilterResultCache.NormalizeList(om), FilterResultCache.NormalizeList(oc), FilterResultCache.NormalizeList(soc),
            FilterResultCache.NormalizeList(od), FilterResultCache.NormalizeMonths(months), FilterResultCache.NormalizeList(jobTitles),
            role, assignedName);
        if (_filterCache.TryGet(cacheKey, out List<StoreComparisonRow>? cachedRows))
            return cachedRows!;

        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var jobs = MultiValueFilter.Split(jobTitles);

        // ── Headcount: average across ALL selected periods per store ──────────
        // Querying each period separately and averaging gives a fair denominator
        // even when the number of months varies (fixes anchor-only bug).
        var allEmpQ = _db.ActiveEmployees.Where(e => keys.Contains(e.Year * 100 + e.Month));
        if (accessible != null)
            allEmpQ = allEmpQ.Where(e => accessible.Contains(e.Store));
        if (jobs != null)
            allEmpQ = allEmpQ.Where(e => jobs.Contains(e.JobTitle));

        var headcountsByPeriod = await allEmpQ
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, Count = g.Count() })
            .ToListAsync();

        // Sum (displayed headcount, range = sum) and average (turnover-rate
        // denominator) per store across the resolved periods.
        var headcounts = headcountsByPeriod
            .GroupBy(x => x.Store)
            .Select(g => new { Store = g.Key, SumCount = g.Sum(x => x.Count), AvgCount = g.Average(x => x.Count) })
            .ToList();

        var resQ = _db.Resignations.Where(r => keys.Contains(r.Year * 100 + r.Month));
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));
        if (jobs != null)
            resQ = resQ.Where(r => jobs.Contains(r.JobTitle));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // New Hires: strictly HireDate-driven, scoped to each row's own snapshot
        // period so an employee who stays active across the whole range isn't
        // counted once per monthly snapshot they appear in.
        var newHireQ = NewHiresQuery(keys);
        if (accessible != null)
            newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));
        if (jobs != null)
            newHireQ = newHireQ.Where(e => jobs.Contains(e.JobTitle));

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
                var res        = resByStore.TryGetValue(h.Store, out var r) ? r : 0;
                var nh         = newHiresByStore.TryGetValue(h.Store, out var n) ? n : 0;
                storeRefs.TryGetValue(h.Store, out var sr);
                return new StoreComparisonRow
                {
                    StoreName           = h.Store,
                    Headcount           = h.SumCount,
                    AvgHeadcount        = h.AvgCount,
                    NewHires            = nh,
                    Resignations        = res,
                    TurnoverRate        = MetricsCalculationService.RatePercent(res, h.AvgCount),
                    OperationConsultant        = sr?.OperationConsultant        ?? "",
                    OperationManager           = sr?.OperationManager           ?? "",
                    SeniorOperationConsultant  = sr?.SeniorOperationConsultant  ?? "",
                    OperationDirector          = sr?.OperationDirector          ?? ""
                };
            });

        if (MultiValueFilter.Split(om) is { } oms) rows = rows.Where(r => oms.Contains(r.OperationManager));
        if (MultiValueFilter.Split(oc) is { } ocs) rows = rows.Where(r => ocs.Contains(r.OperationConsultant));
        if (MultiValueFilter.Split(soc) is { } socs) rows = rows.Where(r => socs.Contains(r.SeniorOperationConsultant));
        if (MultiValueFilter.Split(od) is { } ods) rows = rows.Where(r => ods.Contains(r.OperationDirector));

        var result = rows.OrderByDescending(s => s.TurnoverRate).ToList();
        _filterCache.Set(cacheKey, result, CacheDuration, result.Count);
        return result;
    }

    public async Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var stores = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobTitles);

        OcOmRow ToRow(IGrouping<string, StoreComparisonRow> g, string type) => new()
        {
            Name              = g.Key,
            Type              = type,
            StoreCount        = g.Count(),
            TotalResignations = g.Sum(s => s.Resignations),
            TotalHeadcount    = g.Sum(s => s.Headcount),
            // Uses AvgHeadcount (per-store average across the range), not the
            // summed display Headcount above — a turnover rate needs a fair
            // per-period denominator, not one that inflates with a wider range.
            AvgTurnoverRate   = MetricsCalculationService.RatePercent(g.Sum(s => s.Resignations), g.Sum(s => s.AvgHeadcount))
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

        var socRows = stores
            .Where(s => !string.IsNullOrEmpty(s.SeniorOperationConsultant))
            .GroupBy(s => s.SeniorOperationConsultant)
            .Select(g => ToRow(g, "SOC"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        var odRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationDirector))
            .GroupBy(s => s.OperationDirector)
            .Select(g => ToRow(g, "OD"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        return new OcOmAnalysisResult { OcRows = ocRows, OmRows = omRows, SocRows = socRows, OdRows = odRows };
    }

    // Shifts a resolved period window back by its own length (or back one year for a
    // discrete month selection) so callers can build an apples-to-apples "prior
    // window" of the same length as the current selection. Shared by
    // GetSmartInsightsAsync's trend/spike insights and GetTurnoverTrendAsync.
    private static (int Month, int Year, int? FromMonth, int? FromYear, string? Months) ResolvePreviousWindow(
        int month, int year, int? fromMonth, int? fromYear, string? months)
    {
        var periodCount = ResolvePeriods(month, year, fromMonth, fromYear, months).Count;

        if (!string.IsNullOrWhiteSpace(months))
        {
            // Discrete month selection (e.g. "1,3,5" in 2024) → same months, prior year
            return (month, year - 1, null, null, months);
        }

        // Contiguous range → shift the entire window back by periodCount months
        var anchorShifted = new DateTime(year, month, 1).AddMonths(-periodCount);
        int? prevFromMonth = null, prevFromYear = null;
        if (fromMonth.HasValue && fromYear.HasValue)
        {
            var fromShifted = new DateTime(fromYear.Value, fromMonth.Value, 1).AddMonths(-periodCount);
            prevFromMonth   = fromShifted.Month;
            prevFromYear    = fromShifted.Year;
        }
        return (anchorShifted.Month, anchorShifted.Year, prevFromMonth, prevFromYear, null);
    }

    /// <summary>Company-wide turnover rate for the selected period vs. the
    /// equivalent immediately-prior window (same length) — the Turnover
    /// page's "Trend" KPI card. Reuses the same weighted-average-headcount
    /// rate math as GetStoreComparisonAsync/GetSmartInsightsAsync.</summary>
    public async Task<TurnoverTrendResult> GetTurnoverTrendAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var current = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobTitles);
        var prevWindow = ResolvePreviousWindow(month, year, fromMonth, fromYear, months);
        var previous = await GetStoreComparisonAsync(prevWindow.Month, prevWindow.Year, role, assignedName, prevWindow.FromMonth, prevWindow.FromYear, om, oc, soc, od, prevWindow.Months, jobTitles);

        var currRate = MetricsCalculationService.RatePercent(current.Sum(s => s.Resignations), current.Sum(s => s.AvgHeadcount));
        var prevRate = previous.Any()
            ? MetricsCalculationService.RatePercent(previous.Sum(s => s.Resignations), previous.Sum(s => s.AvgHeadcount))
            : (double?)null;

        return new TurnoverTrendResult
        {
            CurrentRate = currRate,
            PreviousRate = prevRate,
            HasPrevious = previous.Any(),
        };
    }

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var insights = new List<SmartInsightItem>();
        var current  = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobTitles);
        if (!current.Any()) return insights;

        // ── Build the equivalent PREVIOUS window (same length as the current selection) ──
        // This ensures comparisons are apples-to-apples regardless of how many months
        // the user has selected (fixes single-month fallback bug).
        var currentPeriods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var periodCount    = currentPeriods.Count;

        var prevWindow = ResolvePreviousWindow(month, year, fromMonth, fromYear, months);
        var previous    = await GetStoreComparisonAsync(prevWindow.Month, prevWindow.Year, role, assignedName, prevWindow.FromMonth, prevWindow.FromYear, om, oc, soc, od, prevWindow.Months, jobTitles);
        var prevByStore = previous.ToDictionary(s => s.StoreName);

        // Human-readable label for comparison window used in descriptions
        var periodLabel = periodCount == 1
            ? _L["Insight_PeriodLabelLastMonth"].Value
            : string.Format(_L["Insight_PeriodLabelPriorMonths"].Value, periodCount);

        // 1. Highest turnover store
        var highest = current.First();
        if (highest.TurnoverRate > 0)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-exclamation-triangle-fill",
                Color       = "danger",
                Title       = string.Format(_L["Insight_HighestTurnoverTitle"].Value, highest.StoreName),
                Description = string.Format(_L["Insight_HighestTurnoverDesc"].Value, highest.TurnoverRate.ToString("F1"), highest.Resignations, Math.Round(highest.AvgHeadcount)),
                Group       = "primary"
            });

        // 2. Best performing store
        var best = current.Where(s => s.Headcount > 0).OrderBy(s => s.TurnoverRate).FirstOrDefault();
        if (best != null && current.Count > 1 && best.StoreName != highest.StoreName)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-check-circle-fill",
                Color       = "success",
                Title       = string.Format(_L["Insight_BestPerformingTitle"].Value, best.StoreName),
                Description = string.Format(_L["Insight_BestPerformingDesc"].Value, best.TurnoverRate.ToString("F1"), best.Resignations),
                Group       = "primary"
            });

        // 3. Overall trend — compare turnover RATES (not raw counts) so a growing
        //    workforce does not falsely appear as "Worsening". Placed in the same
        //    "primary" row as Highest/Best above.
        if (previous.Any())
        {
            var currRes  = current.Sum(s => s.Resignations);
            var prevRes  = previous.Sum(s => s.Resignations);
            var currHead = current.Sum(s => s.AvgHeadcount);
            var prevHead = previous.Sum(s => s.AvgHeadcount);

            var currRate = MetricsCalculationService.RatePercent(currRes, currHead);
            var prevRate = MetricsCalculationService.RatePercent(prevRes, prevHead);
            var rateDiff = Math.Round(currRate - prevRate, 1);

            insights.Add(new SmartInsightItem
            {
                Icon        = rateDiff > 0 ? "bi-arrow-up-circle-fill" : rateDiff < 0 ? "bi-arrow-down-circle-fill" : "bi-dash-circle-fill",
                Color       = rateDiff > 0 ? "danger" : rateDiff < 0 ? "success" : "secondary",
                Title       = _L[rateDiff > 0 ? "Insight_TrendWorseningTitle" : rateDiff < 0 ? "Insight_TrendImprovingTitle" : "Insight_TrendStableTitle"].Value,
                Description = rateDiff != 0
                    ? string.Format(_L[rateDiff > 0 ? "Insight_TrendIncreasedDesc" : "Insight_TrendDecreasedDesc"].Value, Math.Abs(rateDiff).ToString("F1"), periodLabel, prevRate.ToString("F1"), currRate.ToString("F1"))
                    : string.Format(_L["Insight_TrendUnchangedDesc"].Value, currRate.ToString("F1"), periodLabel),
                Group       = "primary"
            });
        }

        // 4. Highest OC/OM/OD by weighted turnover rate — the "leadership" row.
        // FirstOrDefault() on an empty sequence yields a default tuple (Name
        // null, everything else 0) — checked for below instead of using a
        // nullable tuple, to keep this straightforward.
        (string Name, int StoreCount, int TotalRes, double AvgTurnoverRate) WeightedWorst(Func<StoreComparisonRow, string> keySelector) =>
            current
                .Where(s => !string.IsNullOrEmpty(keySelector(s)))
                .GroupBy(keySelector)
                .Select(g => (Name: g.Key, StoreCount: g.Count(), TotalRes: g.Sum(s => s.Resignations),
                    AvgTurnoverRate: MetricsCalculationService.RatePercent(g.Sum(s => s.Resignations), g.Sum(s => s.AvgHeadcount))))
                .OrderByDescending(g => g.AvgTurnoverRate)
                .FirstOrDefault();

        var worstOc = WeightedWorst(s => s.OperationConsultant);
        if (worstOc.Name != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-fill-exclamation",
                Color       = "warning",
                Title       = string.Format(_L["Insight_HighestOcTurnoverTitle"].Value, worstOc.Name),
                Description = string.Format(_L["Insight_WeightedAvgDesc"].Value, worstOc.AvgTurnoverRate.ToString("F1"), worstOc.StoreCount, worstOc.TotalRes),
                Group       = "leadership"
            });

        var worstOm = WeightedWorst(s => s.OperationManager);
        if (worstOm.Name != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-badge-fill",
                Color       = "warning",
                Title       = string.Format(_L["Insight_HighestOmTurnoverTitle"].Value, worstOm.Name),
                Description = string.Format(_L["Insight_WeightedAvgDesc"].Value, worstOm.AvgTurnoverRate.ToString("F1"), worstOm.StoreCount, worstOm.TotalRes),
                Group       = "leadership"
            });

        var worstOd = WeightedWorst(s => s.OperationDirector);
        if (worstOd.Name != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-vcard-fill",
                Color       = "warning",
                Title       = string.Format(_L["Insight_HighestOdTurnoverTitle"].Value, worstOd.Name),
                Description = string.Format(_L["Insight_WeightedAvgDesc"].Value, worstOd.AvgTurnoverRate.ToString("F1"), worstOd.StoreCount, worstOd.TotalRes),
                Group       = "leadership"
            });

        // 5. Spike detection (>= 5% jump vs equivalent prior window) — the "spike" row.
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
                Title       = string.Format(_L["Insight_TurnoverSpikeTitle"].Value, spike.StoreName),
                Description = string.Format(_L["Insight_TurnoverSpikeDesc"].Value, delta.ToString("F1"), periodLabel, prev.TurnoverRate.ToString("F1"), spike.TurnoverRate.ToString("F1")),
                Group       = "spike"
            });
        }

        return insights;
    }

    public async Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);

        // Headcount per store
        var empQ = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // Resignations per store
        var resQ = _db.Resignations.Where(r => r.Month == month && r.Year == year);
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // New Hires: strictly HireDate-driven — no roster diff against last month.
        var newHireQ = NewHiresQuery(new[] { year * 100 + month });
        if (accessible != null)
            newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));

        var newHireRaw = await newHireQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();
        var newHiresByStore = newHireRaw.ToDictionary(x => x.Store, x => x.Count);

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
                    TurnoverRate = MetricsCalculationService.RatePercent(res, h.Count),
                    NewHires    = nh
                };
            })
            .OrderByDescending(s => s.TurnoverRate)
            .ToList();
    }

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null, string? jobTitles = null, string? store = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);
        var jobs = MultiValueFilter.Split(jobTitles);

        // All available periods ordered chronologically
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        if (sinceYear.HasValue)
            periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

        var monthFilter = MultiValueFilter.Split(months)?.Select(int.Parse).ToHashSet();
        if (monthFilter != null)
            periods = periods.Where(p => monthFilter.Contains(p.Month)).ToList();

        var periodKeys = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        // Headcounts grouped by store + period
        var empQ = _db.ActiveEmployees.AsQueryable();
        if (accessible != null)
            empQ = empQ.Where(e => accessible.Contains(e.Store));
        if (jobs != null)
            empQ = empQ.Where(e => jobs.Contains(e.JobTitle));

        var headcounts = await empQ
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Resignations grouped by store + period
        var resQ = _db.Resignations.AsQueryable();
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));
        if (jobs != null)
            resQ = resQ.Where(r => jobs.Contains(r.JobTitle));

        var resignations = await resQ
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Latest OC/OM assignment per store
        var storeRefList = await LoadLatestStoreReferenceCandidatesAsync();
        var latestRefByStore = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        var ocByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationConsultant ?? "");
        var omByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationManager ?? "");
        var socByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.SeniorOperationConsultant ?? "");
        var odByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationDirector ?? "");

        // Build fast lookups
        var hcLookup  = headcounts .ToDictionary(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}", x => x.Count);
        var resLookup = resignations.GroupBy(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}")
                                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var allStores = headcounts.Select(h => h.Store).Distinct().OrderBy(s => s).ToList();
        if (MultiValueFilter.Split(store) is { } stores) allStores = allStores.Where(s => stores.Contains(s)).ToList();
        if (MultiValueFilter.Split(om) is { } oms) allStores = allStores.Where(s => omByStore.TryGetValue(s, out var v) && oms.Contains(v)).ToList();
        if (MultiValueFilter.Split(oc) is { } ocs) allStores = allStores.Where(s => ocByStore.TryGetValue(s, out var v) && ocs.Contains(v)).ToList();
        if (MultiValueFilter.Split(soc) is { } socs) allStores = allStores.Where(s => socByStore.TryGetValue(s, out var v) && socs.Contains(v)).ToList();
        if (MultiValueFilter.Split(od) is { } ods) allStores = allStores.Where(s => odByStore.TryGetValue(s, out var v) && ods.Contains(v)).ToList();

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
                    var rate = MetricsCalculationService.RatePercent(res, hc);
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
                // Mean of each shown period's own rate, matching the TOTAL
                // column (sum of the same displayed rates) so AVG = TOTAL /
                // number of periods with data.
                AvgRate             = nonNullRates.Count > 0 ? Math.Round(nonNullRates.Average(), 1) : null
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }

    // ── Active-workforce composition (Workforce page) ──────────────────────
    // Snapshots of who currently works here, as opposed to the Turnover-page
    // methods above which describe who resigned.

    public async Task<List<ChartDataItem>> GetHeadcountByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.AsQueryable();
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            if (MultiValueFilter.Split(jobTitles) is { } jobs) qAll = qAll.Where(e => jobs.Contains(e.JobTitle));
            return await qAll.GroupBy(e => e.JobTitle)
                .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .ToListAsync();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        return (await SumBreakdownAsync(periods, accessible, stores, omOcStores, MultiValueFilter.Split(jobTitles), e => e.JobTitle, excludeEmptyKey: false))
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetHeadcountByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.Where(e => e.PayrollGroup != "");
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            if (MultiValueFilter.Split(jobTitles) is { } jobs) qAll = qAll.Where(e => jobs.Contains(e.JobTitle));
            var rowsAll = await qAll.GroupBy(e => e.PayrollGroup)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();
            return rowsAll
                .Select(x => new ChartDataItem { Label = DataLabelTranslator.PayrollGroup(x.Key, _L), Value = x.Count })
                .OrderByDescending(x => x.Value)
                .ToList();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        var summed = await SumBreakdownAsync(periods, accessible, stores, omOcStores, MultiValueFilter.Split(jobTitles), e => e.PayrollGroup, excludeEmptyKey: true);
        return summed
            .Select(c => new ChartDataItem { Label = DataLabelTranslator.PayrollGroup(c.Label, _L), Value = c.Value })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    private static readonly (string Label, int Min, int Max)[] HeadcountTenureBuckets =
    {
        ("< 3 months", 0, 90),
        ("3–6 months", 90, 180),
        ("6–12 months", 180, 365),
        ("1–2 years", 365, 730),
        ("2+ years", 730, int.MaxValue),
    };

    public async Task<List<ChartDataItem>> GetHeadcountByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        List<(int Month, int Year)> periods;
        if (month.HasValue && year.HasValue)
        {
            fromMonth ??= month; fromYear ??= year;
            periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        }
        else
        {
            var latest = await _db.ActiveEmployees
                .OrderByDescending(e => e.Year).ThenByDescending(e => e.Month)
                .Select(e => new { e.Month, e.Year }).FirstOrDefaultAsync();
            if (latest == null) return new List<ChartDataItem>();
            periods = new List<(int Month, int Year)> { (latest.Month, latest.Year) };
        }

        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        // Tenure buckets are computed per period (as-of that period's month end)
        // then SUMMED across periods — range = sum, same as the other
        // composition breakdowns above, rather than anchoring on a single
        // (e.g. latest) month.
        var totals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year && e.HireDate != null);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));
            if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(e => jobs.Contains(e.JobTitle));

            var hireDates = await q.Select(e => e.HireDate!.Value).ToListAsync();
            if (hireDates.Count == 0) continue;

            var asOf = new DateOnly(p.Year, p.Month, DateTime.DaysInMonth(p.Year, p.Month));
            foreach (var b in HeadcountTenureBuckets)
            {
                var count = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max);
                if (count > 0) totals[b.Label] = totals.GetValueOrDefault(b.Label) + count;
            }
        }

        return HeadcountTenureBuckets
            .Where(b => totals.ContainsKey(b.Label))
            .Select(b => new ChartDataItem { Label = b.Label, Value = totals[b.Label] })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetHeadcountTrendAsync(string? store, string role, string? assignedName, string? om, string? oc, string? soc, string? od, int? sinceYear, string? jobTitles = null)
    {
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();
        if (sinceYear.HasValue) periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);
        var stores = MultiValueFilter.Split(store);

        var result = new List<ChartDataItem>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (await GetStoresForOmOcAsync(p.Month, p.Year, om, oc, soc, od) is { } omOcStores) q = q.Where(e => omOcStores.Contains(e.Store));
            if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(e => jobs.Contains(e.JobTitle));

            var count = await q.CountAsync();
            result.Add(new ChartDataItem { Label = $"{p.Year:D4}-{p.Month:D2}", Value = count });
        }
        return result;
    }

    public async Task<List<StoreHeadcountRow>> GetStoreHeadcountBreakdownAsync(int month, int year, string role, string? assignedName, string? om, string? oc, string? soc, string? od, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var omOcStores = await GetStoresForOmOcAsync(month, year, om, oc, soc, od);
        var q = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
        if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(e => jobs.Contains(e.JobTitle));

        // Group by (Store, Gender) and (Store, PayrollGroup) server-side and only
        // pull the per-pair counts — instead of every matching ActiveEmployee row.
        // Same final shape, far fewer rows over the wire.
        var genderGrouped = await q.GroupBy(e => new { e.Store, e.Gender })
            .Select(g => new { g.Key.Store, g.Key.Gender, Count = g.Count() })
            .ToListAsync();
        var payrollGrouped = await q.Where(e => e.PayrollGroup != "").GroupBy(e => new { e.Store, e.PayrollGroup })
            .Select(g => new { g.Key.Store, g.Key.PayrollGroup, Count = g.Count() })
            .ToListAsync();
        var payrollByStore = payrollGrouped.GroupBy(r => r.Store)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PayrollGroup)
                .ToDictionary(x => DataLabelTranslator.PayrollGroup(x.PayrollGroup, _L), x => x.Count));

        return genderGrouped.GroupBy(r => r.Store)
            .Select(g => new StoreHeadcountRow
            {
                StoreName             = g.Key,
                Headcount             = g.Sum(x => x.Count),
                GenderBreakdown       = g.OrderBy(x => x.Gender).ToDictionary(x => DataLabelTranslator.Gender(x.Gender, _L), x => x.Count),
                PayrollGroupBreakdown = payrollByStore.TryGetValue(g.Key, out var pg) ? pg : new Dictionary<string, int>(),
            })
            .OrderByDescending(r => r.Headcount)
            .ToList();
    }

    public async Task<List<StoreLeaderTrackingRow>> GetStoreLeaderTrackingAsync(string store, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);
        if (accessible != null && !accessible.Contains(store)) return new List<StoreLeaderTrackingRow>();

        var snapshots = (await _db.StoreReferences
                .AsNoTracking()
                .Where(s => s.StoreName == store && s.StoreLeader != "")
                .OrderBy(s => s.Year).ThenBy(s => s.Month).ThenBy(s => s.Id)
                .Select(s => new { s.StoreLeader, s.Month, s.Year })
                .ToListAsync())
            .GroupBy(s => (s.Year, s.Month))
            .Select(g => g.First())
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToList();

        if (snapshots.Count == 0) return new List<StoreLeaderTrackingRow>();

        static string Period(int month, int year) =>
            new DateTime(year, month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);

        var result = new List<StoreLeaderTrackingRow>();
        var currentLeader = snapshots[0].StoreLeader;
        var start = (snapshots[0].Month, snapshots[0].Year);
        var previous = start;

        void AddSegment(string leader, (int Month, int Year) from, (int Month, int Year) to)
        {
            var months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
            result.Add(new StoreLeaderTrackingRow
            {
                StoreLeader = leader,
                FromMonth = from.Month,
                FromYear = from.Year,
                ToMonth = to.Month,
                ToYear = to.Year,
                FromPeriod = Period(from.Month, from.Year),
                ToPeriod = Period(to.Month, to.Year),
                Months = months,
            });
        }

        foreach (var snapshot in snapshots.Skip(1))
        {
            var isNextMonth = snapshot.Year * 12 + snapshot.Month == previous.Year * 12 + previous.Month + 1;
            if (!isNextMonth || !string.Equals(snapshot.StoreLeader, currentLeader, StringComparison.OrdinalIgnoreCase))
            {
                AddSegment(currentLeader, start, previous);
                currentLeader = snapshot.StoreLeader;
                start = (snapshot.Month, snapshot.Year);
            }
            previous = (snapshot.Month, snapshot.Year);
        }

        AddSegment(currentLeader, start, previous);
        return result;
    }

    public async Task<List<EmployeeDetailRow>> GetEmployeeDetailsAsync(int month, int year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(month, year, om, oc, soc, od) : null;

        var q = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
        if (stores != null) q = q.Where(e => stores.Contains(e.Store));
        else if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(e => jobs.Contains(e.JobTitle));

        return await q.OrderBy(e => e.Store).ThenBy(e => e.Name)
            .Select(e => new EmployeeDetailRow
            {
                EmployeeId = e.EmployeeId,
                Name = e.Name,
                Store = e.Store,
                JobTitle = e.JobTitle,
                Grade = e.Grade,
                PayrollGroup = e.PayrollGroup,
                Gender = e.Gender,
                HireDate = e.HireDate,
            })
            .ToListAsync();
    }

    public async Task<List<ResignationDetailRow>> GetResignationDetailsAsync(string? store, string role, string? assignedName, string? jobTitles = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);
        var stores = MultiValueFilter.Split(store);

        var q = _db.Resignations.AsQueryable();
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (stores != null) q = q.Where(r => stores.Contains(r.Store));
        if (MultiValueFilter.Split(jobTitles) is { } jobs) q = q.Where(r => jobs.Contains(r.JobTitle));

        return await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(r => r.Name)
            .Select(r => new ResignationDetailRow
            {
                Month = r.Month,
                Year = r.Year,
                EmployeeId = r.EmployeeId,
                Name = r.Name,
                Store = r.Store,
                JobTitle = r.JobTitle,
                Gender = r.Gender,
                HireDate = r.HireDate,
                ResignationDate = r.ResignationDate,
            })
            .ToListAsync();
    }
}
