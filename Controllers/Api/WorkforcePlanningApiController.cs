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
    private readonly IDashboardService _dashboard;

    public WorkforcePlanningApiController(IWorkforcePlanningService planning, INinetyDayTurnoverService ninetyDay, IMemoryCache cache, IDashboardService dashboard)
    {
        _dashboard = dashboard;
        _planning = planning;
        _ninetyDay = ninetyDay;
        _cache = cache;
    }

    /// <summary>One point per store: staffing fill (actual ÷ projected) against turnover over the three
    /// months up to the planned month. Stores without a roster, a projection or headcount are left out.</summary>
    [HttpGet("risk-map")]
    public async Task<IActionResult> RiskMap([FromQuery] int year, [FromQuery] int month, [FromQuery] string? store, [FromQuery] string? jobs,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var plan = await _planning.GetAsync(year, month, store, jobs, role, assignedName, om, oc, soc, od);
        var empty = new { points = Array.Empty<object>(), avgTurnover = 0.0 };
        if (!plan.HasData || !plan.HasActual) return Ok(empty);

        var from = new DateTime(plan.Year, plan.Month, 1).AddMonths(-2);
        List<MvcApp.Models.ViewModels.StoreComparisonRow> turnover;
        try { turnover = await _dashboard.GetStoreComparisonAsync(plan.Month, plan.Year, role, assignedName, from.Month, from.Year, om, oc, soc, od, null, jobs); }
        catch { return Ok(empty); } // no roster for that window: nothing to plot
        var byName = turnover.Where(t => t.AvgHeadcount > 0).ToDictionary(t => t.StoreName.Trim(), StringComparer.OrdinalIgnoreCase);

        var points = plan.ByStore.Where(r => r.Projected > 0 && byName.ContainsKey(r.Name.Trim()))
            .Select(r => { var t = byName[r.Name.Trim()]; return new { store = r.Name, fillPercent = r.FillPercent, turnoverRate = Math.Round(t.TurnoverRate, 1), projected = r.Projected, actual = r.Actual, resignations = t.Resignations, gap = r.Gap }; })
            .ToList();
        var avg = points.Count > 0 ? Math.Round(points.Average(p => p.turnoverRate), 1) : 0;
        return Ok(new { points, avgTurnover = avg });
    }

    /// <summary>The Crew Trainers page (same access area as Workforce Planning).</summary>
    [HttpGet("crew-trainers")]
    public async Task<IActionResult> CrewTrainers([FromServices] ICrewTrainerService trainers, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? store,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await trainers.GetAsync(year, month, store, role, assignedName, om, oc, soc, od));
    }

    [HttpGet("hiring-forecast")]
    public async Task<IActionResult> HiringForecast([FromQuery] int? year, [FromQuery] string? store, [FromQuery] string? jobs,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? by)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od, await EarlyLeaverRate.GetAsync(_ninetyDay, _cache), by));
    }

    [HttpGet("summary")]
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
    public async Task<IActionResult> StoreJobs([FromQuery] string store, [FromQuery] int year, [FromQuery] int month, [FromQuery] string? jobs)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(new { error = "Store is required." });
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var rows = await _planning.GetDetailAsync(year, new[] { month }, store, jobs, role, assignedName);
        return Ok(rows.Select(r => new { job = r.Job, projected = r.Projected, actual = r.Actual, expectedAttrition = r.ExpectedAttrition, hiringNeed = r.HiringNeed })
            .OrderByDescending(r => r.hiringNeed ?? 0).ThenByDescending(r => r.projected).ThenBy(r => r.job, StringComparer.OrdinalIgnoreCase));
    }

    // The two endpoints below feed the Stores and Store Profile pages, which run on the
    // Analytics area, so they follow that area's store visibility rather than this page's.
    [HttpGet("store-fill")]
    [AccessArea(AccessAreas.Analytics)]
    public async Task<IActionResult> StoreFill([FromQuery] int year, [FromQuery] int month, [FromQuery] string? jobs)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetStoreFillAsync(year, month, jobs, role, assignedName));
    }

    [HttpGet("store-plan")]
    [AccessArea(AccessAreas.Analytics)]
    public async Task<IActionResult> StorePlan([FromQuery] string store, [FromQuery] int year, [FromQuery] int month)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(new { error = "Store is required." });
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        return Ok(await _planning.GetStorePlanAsync(store, year, month, role, assignedName));
    }
}
