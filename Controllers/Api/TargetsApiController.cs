using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/targets")]
[RequireAuth]
public class TargetsApiController : ControllerBase
{
    private readonly ITargetsService _targets;

    public TargetsApiController(ITargetsService targets) => _targets = targets;

    public record SetTargetsRequest(double? TurnoverRateTarget, double? Retention90Target);

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _targets.GetAsync());

    [HttpPost]
    [RequireAdminAuth]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set([FromBody] SetTargetsRequest request)
    {
        await _targets.SetAsync(request.TurnoverRateTarget, request.Retention90Target);
        return Ok(await _targets.GetAsync());
    }
}
