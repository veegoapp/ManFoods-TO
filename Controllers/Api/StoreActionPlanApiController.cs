using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/store-action-plan")]
[EnableRateLimiting("api")]
[RequireAuth]
public class StoreActionPlanApiController : ControllerBase
{
    private readonly IStoreActionPlanService _actionPlans;
    private readonly IStoreHealthService _health;
    private readonly IDashboardService _dashboard;
    private readonly IStringLocalizer<SharedResource> _L;

    public StoreActionPlanApiController(IStoreActionPlanService actionPlans, IStoreHealthService health, IDashboardService dashboard, IStringLocalizer<SharedResource> localizer)
    {
        _actionPlans = actionPlans;
        _health = health;
        _dashboard = dashboard;
        _L = localizer;
    }

    // The actual owner of a store's Action Plan (Head_Manager or
    // Operation_Consultant by default, or whichever role an Admin delegated
    // it to on the Action Plan Role settings page) is resolved and checked
    // per-store inside StoreActionPlanService — this attribute is just the
    // outer gate covering every role that could possibly be delegated.
    [HttpPost("{store}/notes"), ValidateAntiForgeryToken]
    [RequireRole("Operation_Manager", "Operation_Consultant", "Head_Manager", "Senior_Operation_Consultant", "Operation_Director")]
    public async Task<IActionResult> AddNote(string store, [FromBody] AddNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.NoteText)) return BadRequest(_L["Api_NoteTextRequired"].Value);

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Unauthorized();

        var authorName = HttpContext.Session.GetAssignedName() ?? email;

        var (success, message, note) = await _actionPlans.AddNoteAsync(store, role, email, userId.Value, authorName, request.NoteText);
        if (!success) return BadRequest(message);

        return Ok(note);
    }

    /// <summary>
    /// Admin-only: runs detection for every period that has uploaded data.
    /// Returns immediately (202 Accepted) and processes in the background so
    /// the request never times out regardless of how many periods/stores exist.
    /// </summary>
    [HttpPost("run-detection"), ValidateAntiForgeryToken]
    public async Task<IActionResult> RunDetectionAllPeriods()
    {
        var role = HttpContext.Session.GetRole();
        if (role != "Admin") return StatusCode(StatusCodes.Status403Forbidden);

        var periods = await _dashboard.GetAvailablePeriodsAsync();
        if (periods.Count == 0) return Accepted(new { status = "no_periods", message = "No periods found." });

        // GetAvailablePeriodsAsync returns newest-first (correct for UI period
        // dropdowns, which this call also depends on elsewhere) — but detection's
        // HealthyStreakCount/LastEvaluatedMonth/auto-resolve logic requires
        // sequential, chronological (oldest→newest) processing per store. Reorder
        // the local copy only; do not touch GetAvailablePeriodsAsync itself.
        var orderedPeriods = periods.OrderBy(p => p.Year).ThenBy(p => p.Month).ToList();

        // Fire-and-forget: respond immediately so the browser doesn't time out,
        // then process every period in the background.
        _ = Task.Run(async () =>
        {
            foreach (var p in orderedPeriods)
            {
                try { await _actionPlans.RunDetectionForPeriodAsync(p.Month, p.Year); }
                catch { /* individual period failure should not stop the rest */ }
            }
        });

        return Accepted(new { status = "started", periods = periods.Count });
    }

    /// <summary>
    /// Admin-only: one-time historical backfill of signal_occurrences for the
    /// 6 signals whose source data is retained per period (excludes the
    /// current-state-only Early Warning Watchlist signal). Runs in the
    /// background and is safe to re-run — see RunHistoricalSignalBackfillAsync.
    /// </summary>
    [HttpPost("backfill-signals"), ValidateAntiForgeryToken]
    public IActionResult RunSignalBackfill()
    {
        var role = HttpContext.Session.GetRole();
        if (role != "Admin") return StatusCode(StatusCodes.Status403Forbidden);

        _ = Task.Run(async () =>
        {
            try { await _actionPlans.RunHistoricalSignalBackfillAsync(); }
            catch { /* best-effort; safe to re-trigger from the Settings page */ }
        });

        return Accepted(new { status = "started" });
    }

    // ────────────────────────────── Action Center ──────────────────────────────

    [HttpGet("action-center/summary")]
    public async Task<IActionResult> GetActionCenterSummary()
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        return Ok(await _actionPlans.GetActionCenterSummaryAsync(role, email));
    }

    [HttpGet("action-center/stores")]
    public async Task<IActionResult> GetActionCenterStores()
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        return Ok(await _actionPlans.GetActionCenterStoresAsync(role, email));
    }

    // ── Store Health engine (the rebuilt Action Center) ──────────────────────

    [HttpGet("action-center/health/summary")]
    public async Task<IActionResult> GetHealthSummary()
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        return Ok(await _health.GetSummaryAsync(role, email));
    }

    [HttpGet("action-center/health/stores")]
    public async Task<IActionResult> GetHealthStores()
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        return Ok(await _health.GetStoreHealthRowsAsync(role, email));
    }

    [HttpGet("action-center/health/detail")]
    public async Task<IActionResult> GetHealthDetail([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(_L["Api_StoreRequired"].Value);
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var result = await _health.GetDetailAsync(store, role, email);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("action-center/detail")]
    public async Task<IActionResult> GetActionCenterDetail([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(_L["Api_StoreRequired"].Value);

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var result = await _actionPlans.GetActionCenterDetailAsync(store, role, email);
        if (result == null) return NotFound();

        return Ok(result);
    }

    /// <summary>Read-only Signal History for the detail page — see
    /// IStoreActionPlanService.GetSignalHistoryAsync.</summary>
    [HttpGet("action-center/signal-history")]
    public async Task<IActionResult> GetSignalHistory([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(_L["Api_StoreRequired"].Value);

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var result = await _actionPlans.GetSignalHistoryAsync(store, role, email);
        if (result == null) return NotFound();

        return Ok(result);
    }

    /// <summary>Read-only Monthly Turnover Performance history for the detail
    /// page — see IStoreActionPlanService.GetMonthlyTurnoverHistoryAsync.</summary>
    [HttpGet("action-center/monthly-turnover")]
    public async Task<IActionResult> GetMonthlyTurnoverHistory([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest(_L["Api_StoreRequired"].Value);

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var result = await _actionPlans.GetMonthlyTurnoverHistoryAsync(store, role, email);
        if (result == null) return NotFound();

        return Ok(result);
    }

    [HttpPost("recommendations/{id:int}/toggle"), ValidateAntiForgeryToken]
    [RequireRole("Admin", "Operation_Manager", "Operation_Consultant", "Head_Manager", "Senior_Operation_Consultant", "Operation_Director")]
    public async Task<IActionResult> ToggleRecommendation(int id, [FromBody] ToggleRecommendationRequest request)
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var actorName = HttpContext.Session.GetAssignedName() ?? email ?? "";

        var success = await _actionPlans.ToggleRecommendationAsync(id, request?.IsCompleted ?? false, role, email, actorName);
        if (!success) return BadRequest(_L["Api_RecommendationNotPermitted"].Value);

        return Ok();
    }

    [HttpPost("{store}/assign"), ValidateAntiForgeryToken]
    [RequireRole("Admin")]
    public async Task<IActionResult> SetAssignment(string store, [FromBody] SetAssignmentRequest request)
    {
        var role = HttpContext.Session.GetRole();
        var success = await _actionPlans.SetAssignmentAsync(store, request?.AssignedToName, request?.TargetResolutionDate, role);
        if (!success) return BadRequest(_L["Api_NoActivePlan"].Value);

        return Ok();
    }

    [HttpPost("{store}/close"), ValidateAntiForgeryToken]
    [RequireRole("Admin")]
    public async Task<IActionResult> ManualClose(string store, [FromBody] ManualCloseRequest request)
    {
        var role = HttpContext.Session.GetRole();
        var closedByName = HttpContext.Session.GetAssignedName() ?? HttpContext.Session.GetEmail() ?? "";

        var (success, message) = await _actionPlans.ManualCloseAsync(store, request?.Reason ?? "", role, closedByName);
        if (!success) return BadRequest(message);

        return Ok();
    }

    public class AddNoteRequest
    {
        public string NoteText { get; set; } = "";
    }

    public class ToggleRecommendationRequest
    {
        public bool IsCompleted { get; set; }
    }

    public class SetAssignmentRequest
    {
        public string? AssignedToName { get; set; }
        public DateOnly? TargetResolutionDate { get; set; }
    }

    public class ManualCloseRequest
    {
        public string? Reason { get; set; }
    }
}
