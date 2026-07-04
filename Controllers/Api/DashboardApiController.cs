using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
[RequireAuth]
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
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var result = await _dashboard.GetKpisAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, months);
        return Ok(result);
    }

    [HttpGet("turnover-by-job-title")]
    public async Task<IActionResult> TurnoverByJobTitle([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTurnoverByJobTitleAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("turnover-by-tenure")]
    public async Task<IActionResult> TurnoverByTenure([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTurnoverByTenureAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("turnover-by-payroll-group")]
    public async Task<IActionResult> TurnoverByPayrollGroup([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTurnoverByPayrollGroupAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("gender-breakdown")]
    public async Task<IActionResult> GenderBreakdown([FromQuery] int? month, [FromQuery] int? year, [FromQuery] string? store,
        [FromQuery] string? om, [FromQuery] string? oc)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetGenderBreakdownAsync(month, year, store, role, assignedName, om, oc));
    }

    [HttpGet("available-periods")]
    public async Task<IActionResult> AvailablePeriods()
    {
        return Ok(await _dashboard.GetAvailablePeriodsAsync());
    }

    [HttpGet("stores")]
    public async Task<IActionResult> Stores([FromQuery] int? month, [FromQuery] int? year)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var stores = await _stores.GetStoresAsync(month, year, role, assignedName);
        return Ok(stores.Select(s => new { storeName = s.StoreName }));
    }

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers([FromQuery] int? month, [FromQuery] int? year)
    {
        return Ok(await _dashboard.GetOperationManagersAsync(month, year));
    }

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants([FromQuery] int? month, [FromQuery] int? year)
    {
        return Ok(await _dashboard.GetOperationConsultantsAsync(month, year));
    }

    [HttpGet("store-comparison")]
    public async Task<IActionResult> StoreComparison([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months);
        return Ok(await _dashboard.GetStoreComparisonAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("oc-om-analysis")]
    public async Task<IActionResult> OcOmAnalysis([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months);
        return Ok(await _dashboard.GetOcOmAnalysisAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("smart-insights")]
    public async Task<IActionResult> SmartInsights([FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var kpis = await _dashboard.GetKpisAsync(month, year, null, role, assignedName, months: months);
        return Ok(await _dashboard.GetSmartInsightsAsync(kpis.Month, kpis.Year, role, assignedName, fromMonth, fromYear, om, oc, months));
    }

    [HttpGet("trend-matrix")]
    public async Task<IActionResult> TrendMatrix([FromQuery] string? om, [FromQuery] string? oc, [FromQuery] int? sinceYear)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        return Ok(await _dashboard.GetTrendMatrixAsync(role, assignedName, om, oc, sinceYear));
    }

    // Role system simplified to Admin/User — no more per-store restriction.
    private Task<bool> CanAccessStoreAsync(string store, string role, string? assignedName) =>
        Task.FromResult(true);

    [HttpGet("store-employees")]
    public async Task<IActionResult> StoreEmployees([FromQuery] string store, [FromQuery] int? month, [FromQuery] int? year)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest();
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        if (!await CanAccessStoreAsync(store, role, assignedName)) return Forbid();
        return Ok(await _dashboard.GetStoreEmployeesAsync(store, month, year));
    }

    [HttpGet("store-resignations")]
    public async Task<IActionResult> StoreResignations([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest();
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        if (!await CanAccessStoreAsync(store, role, assignedName)) return Forbid();
        return Ok(await _dashboard.GetStoreResignationHistoryAsync(store));
    }

}
