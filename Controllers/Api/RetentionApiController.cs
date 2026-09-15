using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/retention")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.Retention)]
public class RetentionApiController : ControllerBase
{
    private readonly IRetentionService _retention;
    private readonly IDashboardService _dashboard;

    public RetentionApiController(IRetentionService retention, IDashboardService dashboard)
    {
        _retention = retention;
        _dashboard = dashboard;
    }

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetStoreListAsync(role, assignedName));
    }

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationManagersAsync(null, null, role, assignedName));
    }

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationConsultantsAsync(null, null, role, assignedName));
    }

    [HttpGet("milestones")]
    public async Task<IActionResult> Milestones([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetMilestonesAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months));
    }

    [HttpGet("survival-curve")]
    public async Task<IActionResult> SurvivalCurve([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetSurvivalCurveAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months));
    }

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? sinceYear)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTrendAsync(store, role, assignedName, om, oc, soc, od, sinceYear));
    }

    [HttpGet("store-leaderboard")]
    public async Task<IActionResult> StoreLeaderboard(
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetStoreLeaderboardAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months));
    }

    [HttpGet("tenure-distribution")]
    public async Task<IActionResult> TenureDistribution([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTenureDistributionAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("tenure-distribution-by-store")]
    public async Task<IActionResult> TenureDistributionByStore([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTenureDistributionByStoreAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("active-tenure-curve")]
    public async Task<IActionResult> ActiveTenureCurve([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetActiveTenureCurveAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("store-retention-ranking")]
    public async Task<IActionResult> StoreRetentionRanking([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetStoreRetentionRankingAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("by-job-title")]
    public async Task<IActionResult> ByJobTitle([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetRetentionByJobTitleAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("by-gender")]
    public async Task<IActionResult> ByGender([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetGenderRetentionAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("average-tenure-by-store")]
    public async Task<IActionResult> AverageTenureByStore([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetAverageTenureByStoreAsync(store, role, assignedName, om, oc, soc, od, month, year));
    }

    [HttpGet("average-tenure-by-manager")]
    public async Task<IActionResult> AverageTenureByManager([FromQuery] string dimension, [FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetAverageTenureByManagerAsync(dimension, role, assignedName, month, year, store, om, oc, soc, od));
    }

    [HttpGet("time-to-first-resignation")]
    public async Task<IActionResult> TimeToFirstResignation([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTimeToFirstResignationDistributionAsync(store, role, assignedName, om, oc, soc, od));
    }

    [HttpGet("monthly-hiring-volume")]
    public async Task<IActionResult> MonthlyHiringVolume([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] int? sinceYear)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetMonthlyHiringVolumeAsync(store, role, assignedName, om, oc, soc, od, sinceYear));
    }

    [HttpGet("senior-operation-consultants")]
    public async Task<IActionResult> SeniorOperationConsultants()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetSeniorOperationConsultantsAsync(null, null, role, assignedName));
    }

    [HttpGet("operation-directors")]
    public async Task<IActionResult> OperationDirectors()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationDirectorsAsync(null, null, role, assignedName));
    }

    [HttpGet("insights")]
    public async Task<IActionResult> Insights([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? soc, [FromQuery] string? od, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetInsightsAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months));
    }
}
