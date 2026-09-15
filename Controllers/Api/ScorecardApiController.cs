using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/scorecard")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.Scorecard)]
public class ScorecardApiController : ControllerBase
{
    private readonly IScorecardService _scorecard;

    public ScorecardApiController(IScorecardService scorecard) => _scorecard = scorecard;

    private static readonly HashSet<string> ValidDimensions = new() { "leader", "oc", "om" };

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string dimension = "leader", [FromQuery] string? om = null,
        [FromQuery] string? oc = null, [FromQuery] string? soc = null, [FromQuery] string? od = null, [FromQuery] string? months = null, [FromQuery] int? year = null)
    {
        if (!ValidDimensions.Contains(dimension)) return BadRequest(new { error = "Invalid dimension." });
        var (role, assignedName) = Identity();
        return Ok(await _scorecard.GetScorecardAsync(dimension, role, assignedName, om, oc, soc, od, months, year));
    }

    [HttpGet("leaders")]
    public async Task<IActionResult> Leaders([FromQuery] string? jobs = null)
    {
        var (role, assignedName) = Identity();
        return Ok(await _scorecard.GetLeaderNamesAsync(role, assignedName));
    }

    [HttpGet("leader-profile")]
    public async Task<IActionResult> LeaderProfile([FromQuery] string leader, [FromQuery] string? months = null, [FromQuery] int? year = null,
        [FromQuery] string dimension = "leader")
    {
        if (string.IsNullOrWhiteSpace(leader)) return BadRequest(new { error = "Leader name is required." });
        if (!ValidDimensions.Contains(dimension)) return BadRequest(new { error = "Invalid dimension." });
        var (role, assignedName) = Identity();
        return Ok(await _scorecard.GetLeaderProfileAsync(leader, role, assignedName, months, year, dimension));
    }

    [HttpGet("leader-history")]
    public async Task<IActionResult> LeaderHistory([FromQuery] string leader, [FromQuery] string? months = null, [FromQuery] int? year = null,
        [FromQuery] string dimension = "leader")
    {
        if (string.IsNullOrWhiteSpace(leader)) return BadRequest(new { error = "Leader name is required." });
        if (!ValidDimensions.Contains(dimension)) return BadRequest(new { error = "Invalid dimension." });
        var (role, assignedName) = Identity();
        return Ok(await _scorecard.GetLeaderHistoryAsync(leader, role, assignedName, months, year, dimension));
    }

    [HttpGet("rollup")]
    public async Task<IActionResult> Rollup([FromQuery] string? om = null, [FromQuery] string? oc = null,
        [FromQuery] string? soc = null, [FromQuery] string? od = null, [FromQuery] string? months = null, [FromQuery] int? year = null)
    {
        var (role, assignedName) = Identity();
        return Ok(await _scorecard.GetRollupAsync(role, assignedName, om, oc, soc, od, months, year));
    }
}
