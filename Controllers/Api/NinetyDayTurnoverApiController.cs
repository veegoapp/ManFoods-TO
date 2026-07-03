using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ninety-day-turnover")]
[RequireAdminAuth]
public class NinetyDayTurnoverApiController : ControllerBase
{
    private readonly INinetyDayTurnoverService _turnover;

    public NinetyDayTurnoverApiController(INinetyDayTurnoverService turnover) => _turnover = turnover;

    [HttpGet("cohort-periods")]
    public async Task<IActionResult> CohortPeriods() => Ok(await _turnover.GetCohortPeriodsAsync());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores() => Ok(await _turnover.GetStoreListAsync());

    [HttpGet("kpi")]
    public async Task<IActionResult> Kpi([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store) =>
        Ok(await _turnover.GetKpiAsync(month, year, store));

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store) => Ok(await _turnover.GetTrendAsync(store));

    [HttpGet("by-store")]
    public async Task<IActionResult> ByStore([FromQuery] int month, [FromQuery] int year) =>
        Ok(await _turnover.GetByStoreAsync(month, year));

    [HttpGet("early-leavers")]
    public async Task<IActionResult> EarlyLeavers([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store) =>
        Ok(await _turnover.GetEarlyLeaversAsync(month, year, store));

    [HttpGet("reasons")]
    public async Task<IActionResult> Reasons([FromQuery] int month, [FromQuery] int year, [FromQuery] string? store) =>
        Ok(await _turnover.GetEarlyLeaverReasonsAsync(month, year, store));
}
