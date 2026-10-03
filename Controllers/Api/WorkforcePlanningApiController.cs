using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IMemoryCache _cache;

    public WorkforcePlanningApiController(IWorkforcePlanningService planning, INinetyDayTurnoverService ninetyDay, IMemoryCache cache)
    {
        _planning = planning;
        _ninetyDay = ninetyDay;
        _cache = cache;
    }

    /// <summary>The Crew Trainers page (its own access area).</summary>
    [HttpGet("crew-trainers")]
    [AccessArea(AccessAreas.CrewTrainers)]
    [RequiresAnyPage("crewtrainers")]
    public async Task<IActionResult> CrewTrainers([FromServices] ICrewTrainerService trainers, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? store,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await trainers.GetAsync(year, month, store, role, assignedName, om, oc, soc, od));
    }

    [HttpGet("hiring-forecast")]
    [AccessArea(AccessAreas.HiringForecast)]
    [RequiresAnyPage("hiringforecast")]
    public async Task<IActionResult> HiringForecast([FromQuery] int? year, [FromQuery] string? store, [FromQuery] string? jobs,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? by)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od, await EarlyLeaverRate.GetAsync(_ninetyDay, _cache), by));
    }

    [HttpGet("summary")]
    [RequiresAnyPage("workforceplanning")]
    public async Task<IActionResult> Summary([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? store, [FromQuery] string? jobs,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetAsync(year, month, store, jobs, role, assignedName, om, oc, soc, od));
    }

    /// <summary>One store's jobs for a month (projected, actual, expected resignations, hiring need): the
    /// breakdown behind the store's row in the stores table.</summary>
    [HttpGet("store-jobs")]
    [RequiresAnyPage("workforceplanning")]
    public async Task<IActionResult> StoreJobs([FromQuery] string store, [FromQuery] int year, [FromQuery] int month, [FromQuery] string? jobs)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(new { error = "Store is required." });
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var rows = await _planning.GetDetailAsync(year, new[] { month }, store, jobs, role, assignedName);
        return Ok(rows.Select(r => new { job = r.Job, projected = r.Projected, actual = r.Actual, shortage = r.Shortage, expectedAttrition = r.ExpectedAttrition, hiringNeed = r.HiringNeed })
            .OrderByDescending(r => r.hiringNeed ?? 0).ThenByDescending(r => r.projected).ThenBy(r => r.job, StringComparer.OrdinalIgnoreCase));
    }

    // The two endpoints below feed the Stores and Store Profile pages, which run on the
    // Analytics area, so they follow that area's store visibility rather than this page's.
    [HttpGet("store-fill")]
    [RequiresAnyPage("stores")]
    [AccessArea(AccessAreas.Analytics)]
    public async Task<IActionResult> StoreFill([FromQuery] int year, [FromQuery] int month, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetStoreFillAsync(year, month, jobs, role, assignedName));
    }

    [HttpGet("store-plan")]
    [RequiresAnyPage("stores")]
    [AccessArea(AccessAreas.Analytics)]
    public async Task<IActionResult> StorePlan([FromQuery] string store, [FromQuery] int year, [FromQuery] int month)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(new { error = "Store is required." });
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetStorePlanAsync(store, year, month, role, assignedName));
    }
}
