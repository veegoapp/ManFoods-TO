using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ninety-day-turnover")]
[RequireAuth]
public class NinetyDayTurnoverApiController : ControllerBase
{
    private readonly INinetyDayTurnoverService _turnover;
    private readonly IDashboardService _dashboard;

    public NinetyDayTurnoverApiController(INinetyDayTurnoverService turnover, IDashboardService dashboard)
    {
        _turnover = turnover;
        _dashboard = dashboard;
    }

    [HttpGet("cohort-periods")]
    public async Task<IActionResult> CohortPeriods() => Ok(await _turnover.GetCohortPeriodsAsync());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores() => Ok(await _turnover.GetStoreListAsync());

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers() => Ok(await _dashboard.GetOperationManagersAsync(null, null));

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants() => Ok(await _dashboard.GetOperationConsultantsAsync(null, null));

    [HttpGet("kpi")]
    public async Task<IActionResult> Kpi([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _turnover.GetKpiAsync(month, year, store, fromMonth, fromYear, om, oc, months));

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc) =>
        Ok(await _turnover.GetTrendAsync(store, om, oc));

    [HttpGet("by-store")]
    public async Task<IActionResult> ByStore([FromQuery] int month, [FromQuery] int year,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _turnover.GetByStoreAsync(month, year, fromMonth, fromYear, om, oc, months));

    [HttpGet("early-leavers")]
    public async Task<IActionResult> EarlyLeavers([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _turnover.GetEarlyLeaversAsync(month, year, store, fromMonth, fromYear, om, oc, months));

    [HttpGet("reasons")]
    public async Task<IActionResult> Reasons([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _turnover.GetEarlyLeaverReasonsAsync(month, year, store, fromMonth, fromYear, om, oc, months));

    [HttpGet("trend-matrix")]
    public async Task<IActionResult> TrendMatrix([FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months, [FromQuery] int? sinceYear) =>
        Ok(await _turnover.GetTrendMatrixAsync(om, oc, months, sinceYear));
}
