using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.Analytics)] // Workforce + Turnover + Comparisons share this backend
public class DashboardApiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IStoreService _stores;

    public DashboardApiController(IDashboardService dashboard, IStoreService stores)
    {
        _dashboard = dashboard;
        _stores = stores;
    }

    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var result = await _dashboard.GetKpisAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs);
        return Ok(result);
    }

    [HttpGet("turnover-by-job-title")]
    public async Task<IActionResult> TurnoverByJobTitle([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetTurnoverByJobTitleAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("turnover-by-tenure")]
    public async Task<IActionResult> TurnoverByTenure([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetTurnoverByTenureAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("turnover-by-payroll-group")]
    public async Task<IActionResult> TurnoverByPayrollGroup([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetTurnoverByPayrollGroupAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("gender-breakdown")]
    public async Task<IActionResult> GenderBreakdown([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetGenderBreakdownAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("available-periods")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> AvailablePeriods()
    {
        return Ok(await _dashboard.GetAvailablePeriodsAsync());
    }

    [HttpGet("stores")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> Stores([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var stores = await _stores.GetStoresAsync(month, year, role, assignedName);
        return Ok(stores.Select(s => new { storeName = s.StoreName }));
    }

    [HttpGet("operation-managers")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> OperationManagers([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetOperationManagersAsync(month, year, role, assignedName));
    }

    [HttpGet("operation-consultants")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> OperationConsultants([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetOperationConsultantsAsync(month, year, role, assignedName));
    }

    [HttpGet("store-comparison")]
    public async Task<IActionResult> StoreComparison([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months, jobTitles: jobs);
        return Ok(await _dashboard.GetStoreComparisonAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("oc-om-analysis")]
    public async Task<IActionResult> OcOmAnalysis([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months, jobTitles: jobs);
        return Ok(await _dashboard.GetOcOmAnalysisAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("smart-insights")]
    public async Task<IActionResult> SmartInsights([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months, jobTitles: jobs);
        return Ok(await _dashboard.GetSmartInsightsAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("turnover-trend")]
    public async Task<IActionResult> TurnoverTrend([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months, jobTitles: jobs);
        return Ok(await _dashboard.GetTurnoverTrendAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("trend-matrix")]
    public async Task<IActionResult> TrendMatrix([FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? sinceYear, [FromQuery] string? months, [FromQuery] string? jobs, [FromQuery] string? store)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, sinceYear, months, jobs, store));
    }

    [HttpGet("headcount-by-job-title")]
    public async Task<IActionResult> HeadcountByJobTitle([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetHeadcountByJobTitleAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("headcount-by-payroll-group")]
    public async Task<IActionResult> HeadcountByPayrollGroup([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetHeadcountByPayrollGroupAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("headcount-by-tenure")]
    public async Task<IActionResult> HeadcountByTenure([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetHeadcountByTenureAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months, jobs));
    }

    [HttpGet("headcount-trend")]
    public async Task<IActionResult> HeadcountTrend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? sinceYear, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetHeadcountTrendAsync(store, role, assignedName, om, oc, soc, od, sinceYear, jobs));
    }

    [HttpGet("store-headcount-breakdown")]
    public async Task<IActionResult> StoreHeadcountBreakdown([FromQuery] int month, [FromQuery] int year, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetStoreHeadcountBreakdownAsync(month, year, role, assignedName, om, oc, soc, od, jobs));
    }

    [HttpGet("store-leader-tracking")]
    public async Task<IActionResult> StoreLeaderTracking([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(new { error = "Store is required." });
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetStoreLeaderTrackingAsync(store, role, assignedName));
    }

    [HttpGet("senior-operation-consultants")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> SeniorOperationConsultants([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetSeniorOperationConsultantsAsync(month, year, role, assignedName));
    }

    [HttpGet("operation-directors")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> OperationDirectors([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetOperationDirectorsAsync(month, year, role, assignedName));
    }

    [HttpGet("job-titles")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> JobTitles([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _dashboard.GetJobTitlesAsync(month, year, role, assignedName));
    }

}
