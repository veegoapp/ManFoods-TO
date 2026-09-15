using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/exit-interviews")]
[EnableRateLimiting("api")]
[RequireAuth]
[AccessArea(AccessAreas.ExitInterviews)]
public class ExitInterviewApiController : ControllerBase
{
    private readonly IExitInterviewService _exitInterviews;

    public ExitInterviewApiController(IExitInterviewService exitInterviews) => _exitInterviews = exitInterviews;

    private ExitInterviewFilter BuildFilter(string? store, string? storeLeader, string? oc, string? om, int? year, string? months, string? soc = null, string? od = null) =>
        new()
        {
            Store = store,
            StoreLeader = storeLeader,
            OperationConsultant = oc,
            OperationManager = om,
            SeniorOperationConsultant = soc,
            OperationDirector = od,
            Year = year,
            Months = months,
            Jobs = Request.Query["jobs"].ToString()
        };

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet("filters")]
    public async Task<IActionResult> Filters()
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetFilterOptionsAsync(role, assignedName));
    }

    [HttpGet("available-periods")]
    public async Task<IActionResult> AvailablePeriods() => Ok(await _exitInterviews.GetAvailablePeriodsAsync());

    [HttpGet("reasons")]
    public async Task<IActionResult> Reasons([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] string? soc, [FromQuery] string? od,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetReasonsForLeavingAsync(BuildFilter(store, storeLeader, oc, om, year, months, soc, od), role, assignedName));
    }

    [HttpGet("would-return")]
    public async Task<IActionResult> WouldReturn([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] string? soc, [FromQuery] string? od,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetWouldReturnAsync(BuildFilter(store, storeLeader, oc, om, year, months, soc, od), role, assignedName));
    }

    [HttpGet("overall-experience")]
    public async Task<IActionResult> OverallExperience([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] string? soc, [FromQuery] string? od,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetOverallExperienceAsync(BuildFilter(store, storeLeader, oc, om, year, months, soc, od), role, assignedName));
    }

    [HttpGet("workload")]
    public async Task<IActionResult> Workload([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] string? soc, [FromQuery] string? od,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetWorkloadConditionAsync(BuildFilter(store, storeLeader, oc, om, year, months, soc, od), role, assignedName));
    }

    [HttpGet("training")]
    public async Task<IActionResult> Training([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetTrainingAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("fair-treatment")]
    public async Task<IActionResult> FairTreatment([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetFairTreatmentAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("work-pressure-reason")]
    public async Task<IActionResult> WorkPressureReason([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetWorkPressureReasonAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("engagement-drivers")]
    public async Task<IActionResult> EngagementDrivers([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetEngagementDriversAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("by-job-title")]
    public async Task<IActionResult> ByJobTitle([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetByJobTitleAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("reasons-trend")]
    public async Task<IActionResult> ReasonsTrend([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetReasonsTrendAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("reason-vs-would-return")]
    public async Task<IActionResult> ReasonVsWouldReturn([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetReasonVsWouldReturnAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("sentiment-summary")]
    public async Task<IActionResult> SentimentSummary([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] string? soc, [FromQuery] string? od,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetSentimentSummaryAsync(BuildFilter(store, storeLeader, oc, om, year, months, soc, od), role, assignedName));
    }

    [HttpGet("comments")]
    [AccessArea(AccessAreas.ExitComments)]
    public async Task<IActionResult> Comments([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetCommentsAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }
}
