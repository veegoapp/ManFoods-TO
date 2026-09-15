using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/early-warning")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.EarlyWarning)]
public class EarlyWarningApiController : ControllerBase
{
    private readonly IEarlyWarningService _earlyWarning;

    public EarlyWarningApiController(IEarlyWarningService earlyWarning) => _earlyWarning = earlyWarning;

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
    {
        var (role, assignedName) = Identity();
        return Ok(await _earlyWarning.GetStoreListAsync(role, assignedName));
    }

    [HttpGet("watchlist")]
    public async Task<IActionResult> Watchlist([FromQuery] string? store, [FromQuery] string? months, [FromQuery] int? year,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? jobs)
    {
        var (role, assignedName) = Identity();
        return Ok(await _earlyWarning.GetWatchlistAsync(store, role, assignedName, months, year, om, oc, soc, od));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] string? store, [FromQuery] string? months, [FromQuery] int? year,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? jobs)
    {
        var (role, assignedName) = Identity();
        return Ok(await _earlyWarning.GetSummaryAsync(store, role, assignedName, months, year, om, oc, soc, od));
    }
}
