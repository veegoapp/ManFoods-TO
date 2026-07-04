using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/retention")]
[RequireAuth]
public class RetentionApiController : ControllerBase
{
    private readonly IRetentionService _retention;
    private readonly IDashboardService _dashboard;

    public RetentionApiController(IRetentionService retention, IDashboardService dashboard)
    {
        _retention = retention;
        _dashboard = dashboard;
    }

    [HttpGet("stores")]
    public async Task<IActionResult> Stores() => Ok(await _retention.GetStoreListAsync());

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers() => Ok(await _dashboard.GetOperationManagersAsync(null, null));

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants() => Ok(await _dashboard.GetOperationConsultantsAsync(null, null));

    [HttpGet("milestones")]
    public async Task<IActionResult> Milestones([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _retention.GetMilestonesAsync(store, fromMonth, fromYear, toMonth, toYear, om, oc, months));

    [HttpGet("survival-curve")]
    public async Task<IActionResult> SurvivalCurve([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _retention.GetSurvivalCurveAsync(store, fromMonth, fromYear, toMonth, toYear, om, oc, months));

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] int? sinceYear) =>
        Ok(await _retention.GetTrendAsync(store, om, oc, sinceYear));

    [HttpGet("store-leaderboard")]
    public async Task<IActionResult> StoreLeaderboard(
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _retention.GetStoreLeaderboardAsync(fromMonth, fromYear, toMonth, toYear, om, oc, months));

    [HttpGet("tenure-distribution")]
    public async Task<IActionResult> TenureDistribution([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc) =>
        Ok(await _retention.GetTenureDistributionAsync(store, om, oc));

    [HttpGet("tenure-distribution-by-store")]
    public async Task<IActionResult> TenureDistributionByStore([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc) =>
        Ok(await _retention.GetTenureDistributionByStoreAsync(store, om, oc));

    [HttpGet("insights")]
    public async Task<IActionResult> Insights([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months) =>
        Ok(await _retention.GetInsightsAsync(store, fromMonth, fromYear, toMonth, toYear, om, oc, months));
}
