using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/exit-interviews")]
[RequireAuth]
public class ExitInterviewApiController : ControllerBase
{
    private readonly IExitInterviewService _exitInterviews;

    public ExitInterviewApiController(IExitInterviewService exitInterviews) => _exitInterviews = exitInterviews;

    private static ExitInterviewFilter BuildFilter(string? store, string? storeLeader, string? oc, string? om, int? year, string? months) =>
        new() { Store = store, StoreLeader = storeLeader, OperationConsultant = oc, OperationManager = om, Year = year, Months = months };

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetAssignedName());

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
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetReasonsForLeavingAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("would-return")]
    public async Task<IActionResult> WouldReturn([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetWouldReturnAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("overall-experience")]
    public async Task<IActionResult> OverallExperience([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetOverallExperienceAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }

    [HttpGet("workload")]
    public async Task<IActionResult> Workload([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetWorkloadConditionAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
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

    [HttpGet("comments")]
    public async Task<IActionResult> Comments([FromQuery] string? store, [FromQuery] string? storeLeader, [FromQuery] string? oc, [FromQuery] string? om,
        [FromQuery] int? year, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _exitInterviews.GetCommentsAsync(BuildFilter(store, storeLeader, oc, om, year, months), role, assignedName));
    }
}
