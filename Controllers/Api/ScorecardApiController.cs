using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/scorecard")]
[RequireAdminAuth]
public class ScorecardApiController : ControllerBase
{
    private readonly IScorecardService _scorecard;

    public ScorecardApiController(IScorecardService scorecard) => _scorecard = scorecard;

    private static readonly HashSet<string> ValidDimensions = new() { "leader", "oc", "om" };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string dimension = "leader")
    {
        if (!ValidDimensions.Contains(dimension)) return BadRequest(new { error = "Invalid dimension." });
        return Ok(await _scorecard.GetScorecardAsync(dimension));
    }
}
