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

    public RetentionApiController(IRetentionService retention) => _retention = retention;

    [HttpGet("stores")]
    public async Task<IActionResult> Stores() => Ok(await _retention.GetStoreListAsync());

    [HttpGet("milestones")]
    public async Task<IActionResult> Milestones([FromQuery] string? store) => Ok(await _retention.GetMilestonesAsync(store));

    [HttpGet("survival-curve")]
    public async Task<IActionResult> SurvivalCurve([FromQuery] string? store) => Ok(await _retention.GetSurvivalCurveAsync(store));

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store) => Ok(await _retention.GetTrendAsync(store));

    [HttpGet("store-leaderboard")]
    public async Task<IActionResult> StoreLeaderboard() => Ok(await _retention.GetStoreLeaderboardAsync());

    [HttpGet("tenure-distribution")]
    public async Task<IActionResult> TenureDistribution([FromQuery] string? store) => Ok(await _retention.GetTenureDistributionAsync(store));

    [HttpGet("insights")]
    public async Task<IActionResult> Insights([FromQuery] string? store) => Ok(await _retention.GetInsightsAsync(store));
}
