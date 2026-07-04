using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/ai-assistant")]
[RequireAuth]
public class AiAssistantApiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IRetentionService _retention;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IExitInterviewService _exitInterviews;
    private readonly IAiUsageService _usage;
    private readonly IGeminiService _gemini;

    public AiAssistantApiController(
        IDashboardService dashboard,
        IRetentionService retention,
        INinetyDayTurnoverService ninetyDay,
        IExitInterviewService exitInterviews,
        IAiUsageService usage,
        IGeminiService gemini)
    {
        _dashboard = dashboard;
        _retention = retention;
        _ninetyDay = ninetyDay;
        _exitInterviews = exitInterviews;
        _usage = usage;
        _gemini = gemini;
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage()
    {
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Unauthorized();
        var (used, limit) = await _usage.GetUsageAsync(userId.Value);
        return Ok(new { used, limit });
    }

    public record ChatRequest(string Question, int? Month, int? Year, string? Store);

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "السؤال لا يمكن أن يكون فارغًا." });

        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Unauthorized();

        var (allowed, used, limit) = await _usage.TryRecordUsageAsync(userId.Value);
        if (!allowed)
            return StatusCode(429, new { error = $"وصلت لأقصى عدد أسئلة مسموح بيه النهاردة ({limit}). حاول تاني بكرة.", used, limit });

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
            RetentionMilestones = milestones.Where(m => m.TotalHires > 0).Select(m => (m.Label, m.RetentionRate)).ToList(),
            NinetyDayCohorts    = ninetyDayTrend.TakeLast(12).Select(c => (c.Label, c.Rate, c.IsProvisional)).ToList(),
            ExitInterviewReasons = exitReasons.Select(r => (r.Label, r.Value)).ToList(),
        };

        var answer = await _gemini.AskAsync(request.Question, context);
        await _usage.RecordTokensAsync(userId.Value, answer.PromptTokens, answer.CompletionTokens);
        return Ok(new { answer = answer.Text, used, limit });
    }
}
