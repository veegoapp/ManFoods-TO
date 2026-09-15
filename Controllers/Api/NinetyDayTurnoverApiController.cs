using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ninety-day-turnover")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.NinetyDay)]
public class NinetyDayTurnoverApiController : ControllerBase
{
    private readonly INinetyDayTurnoverService _turnover;
    private readonly IDashboardService _dashboard;

    public NinetyDayTurnoverApiController(INinetyDayTurnoverService turnover, IDashboardService dashboard)
    {
        _turnover = turnover;
        _dashboard = dashboard;
    }

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet("cohort-periods")]
    public async Task<IActionResult> CohortPeriods() => Ok(await _turnover.GetCohortPeriodsAsync());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetStoreListAsync(role, assignedName));
    }

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationManagersAsync(null, null, role, assignedName));
    }

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationConsultantsAsync(null, null, role, assignedName));
    }

    [HttpGet("kpi")]
    public async Task<IActionResult> Kpi([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetKpiAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetTrendAsync(store, role, assignedName, om, oc, soc, od));
    }

    [HttpGet("by-store")]
    public async Task<IActionResult> ByStore([FromQuery] int month, [FromQuery] int year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetByStoreAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("store-comparison")]
    public async Task<IActionResult> StoreComparison([FromQuery] int month, [FromQuery] int year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("oc-om-analysis")]
    public async Task<IActionResult> OcOmAnalysis([FromQuery] int month, [FromQuery] int year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetOcOmAnalysisAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("job-titles")]
    public async Task<IActionResult> JobTitles([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetEarlyLeaverJobTitlesAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("payroll-groups")]
    public async Task<IActionResult> PayrollGroups([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetEarlyLeaverPayrollGroupsAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("gender-breakdown")]
    public async Task<IActionResult> GenderBreakdown([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetEarlyLeaverGenderBreakdownAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("smart-insights")]
    public async Task<IActionResult> SmartInsights([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetSmartInsightsAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("early-leavers")]
    [AccessArea(AccessAreas.NinetyDayLeavers)]
    public async Task<IActionResult> EarlyLeavers([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetEarlyLeaversAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("reasons")]
    public async Task<IActionResult> Reasons([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetEarlyLeaverReasonsAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months));
    }

    [HttpGet("trend-matrix")]
    public async Task<IActionResult> TrendMatrix([FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months, [FromQuery] int? sinceYear, [FromQuery] string? store)
    {
        var (role, assignedName) = Identity();
        return Ok(await _turnover.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, months, sinceYear, store));
    }

    [HttpGet("senior-operation-consultants")]
    public async Task<IActionResult> SeniorOperationConsultants()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetSeniorOperationConsultantsAsync(null, null, role, assignedName));
    }

    [HttpGet("operation-directors")]
    public async Task<IActionResult> OperationDirectors()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationDirectorsAsync(null, null, role, assignedName));
    }
}
