using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/scorecard")]
[RequireAuth]
public class ScorecardApiController : ControllerBase
{
    private readonly IScorecardService _scorecard;

    public ScorecardApiController(IScorecardService scorecard) => _scorecard = scorecard;

    private static readonly HashSet<string> ValidDimensions = new() { "leader", "oc", "om" };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string dimension = "leader", [FromQuery] string? om = null,
        [FromQuery] string? oc = null, [FromQuery] string? months = null, [FromQuery] int? year = null)
    {
        if (!ValidDimensions.Contains(dimension)) return BadRequest(new { error = "Invalid dimension." });
        return Ok(await _scorecard.GetScorecardAsync(dimension, om, oc, months, year));
    }

    [HttpGet("leaders")]
    public async Task<IActionResult> Leaders() => Ok(await _scorecard.GetLeaderNamesAsync());

    [HttpGet("leader-history")]
    public async Task<IActionResult> LeaderHistory([FromQuery] string leader, [FromQuery] string? months = null, [FromQuery] int? year = null)
    {
        if (string.IsNullOrWhiteSpace(leader)) return BadRequest(new { error = "Leader name is required." });
        return Ok(await _scorecard.GetLeaderHistoryAsync(leader, months, year));
    }

    [HttpGet("rollup")]
    public async Task<IActionResult> Rollup([FromQuery] string? months = null, [FromQuery] int? year = null) =>
        Ok(await _scorecard.GetRollupAsync(months, year));
}
