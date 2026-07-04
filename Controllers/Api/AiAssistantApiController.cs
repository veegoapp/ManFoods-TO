using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ai-assistant")]
[RequireAdminAuth]
public class AiAssistantApiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IRetentionService _retention;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IExitInterviewService _exitInterviews;
    private readonly ITargetsService _targets;
    private readonly IGeminiService _gemini;

    public AiAssistantApiController(
        IDashboardService dashboard,
        IRetentionService retention,
        INinetyDayTurnoverService ninetyDay,
        IExitInterviewService exitInterviews,
        ITargetsService targets,
        IGeminiService gemini)
    {
        _dashboard = dashboard;
        _retention = retention;
        _ninetyDay = ninetyDay;
        _exitInterviews = exitInterviews;
        _targets = targets;
        _gemini = gemini;
    }

    public record ChatRequest(string Question, int? Month, int? Year, string? Store);

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "السؤال لا يمكن أن يكون فارغًا." });

        const string role = "Admin";
        string? assignedName = null;

        var kpis = await _dashboard.GetKpisAsync(request.Month, request.Year, request.Store, role, assignedName);

        // Run sequentially — EF Core DbContext does not support concurrent queries
        var jobTitleData = await _dashboard.GetTurnoverByJobTitleAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);
        var tenureData   = await _dashboard.GetTurnoverByTenureAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);
        var genderData   = await _dashboard.GetGenderBreakdownAsync(kpis.Month, kpis.Year, request.Store, role, assignedName);

        var storeData = string.IsNullOrEmpty(request.Store)
            ? await _dashboard.GetPerStoreTurnoverAsync(kpis.Month, kpis.Year, role, assignedName)
            : new List<StoreBreakdown>();

        var milestones = await _retention.GetMilestonesAsync(request.Store);
        var ninetyDayTrend = await _ninetyDay.GetTrendAsync(request.Store);
        var exitReasons = await _exitInterviews.GetReasonsForLeavingAsync(new ExitInterviewFilter { Store = request.Store }, role, assignedName);
        var targets = await _targets.GetAsync();

        var context = new GeminiContext
        {
            Month             = kpis.Month,
            Year              = kpis.Year,
            Store             = request.Store,
            TotalHeadcount    = kpis.TotalHeadcount,
            TotalResignations = kpis.TotalResignations,
            TurnoverRate      = kpis.TurnoverRate,
            NewHires          = kpis.NewHires,
            StoreBreakdowns    = storeData,
            TurnoverByJobTitle = jobTitleData.Select(x => (x.Label, x.Value)).ToList(),
            TurnoverByTenure   = tenureData.Select(x => (x.Label, x.Value)).ToList(),
            GenderBreakdown    = genderData.Select(x => (x.Label, x.Value)).ToList(),
            RetentionMilestones = milestones.Where(m => m.TotalHires > 0).Select(m => (m.Days, m.RetentionRate)).ToList(),
            NinetyDayCohorts    = ninetyDayTrend.TakeLast(12).Select(c => (c.Label, c.Rate, c.IsProvisional)).ToList(),
            ExitInterviewReasons = exitReasons.Select(r => (r.Label, r.Value)).ToList(),
            TurnoverRateTarget = targets.TurnoverRateTarget,
            Retention90Target = targets.Retention90Target,
        };

        var answer = await _gemini.AskAsync(request.Question, context);
        return Ok(new { answer });
    }
}
