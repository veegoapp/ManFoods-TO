using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/workforce-planning")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.WorkforcePlanning)]
public class WorkforcePlanningApiController : ControllerBase
{
    private readonly IWorkforcePlanningService _planning;

    public WorkforcePlanningApiController(IWorkforcePlanningService planning) => _planning = planning;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? store, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetAsync(year, month, store, jobs, role, assignedName));
    }
}
