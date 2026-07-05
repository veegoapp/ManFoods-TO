using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/early-warning")]
[RequireAuth]
public class EarlyWarningApiController : ControllerBase
{
    private readonly IEarlyWarningService _earlyWarning;

    public EarlyWarningApiController(IEarlyWarningService earlyWarning) => _earlyWarning = earlyWarning;

    [HttpGet("stores")]
    public async Task<IActionResult> Stores() => Ok(await _earlyWarning.GetStoreListAsync());

    [HttpGet("watchlist")]
    public async Task<IActionResult> Watchlist([FromQuery] string? store, [FromQuery] string? months, [FromQuery] int? year) =>
        Ok(await _earlyWarning.GetWatchlistAsync(store, months, year));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] string? store, [FromQuery] string? months, [FromQuery] int? year) =>
        Ok(await _earlyWarning.GetSummaryAsync(store, months, year));
}
