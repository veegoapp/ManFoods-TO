using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

/// <summary>Feed and "Clear History" behind the Activity Logs page. Super Admin only — enforced server-side on every
/// action by <see cref="RequireSuperAdminAttribute"/>, never by hiding a link.</summary>
[ApiController]
[Route("api/activity-logs")]
[EnableRateLimiting("api")]
[RequireAuth]
public class ActivityLogsApiController : ControllerBase
{
    private readonly IActivityLogService _logs;
    private readonly IActivityLogWriter _writer;
    private readonly IStringLocalizer<SharedResource> _L;
    private readonly ILogger<ActivityLogsApiController> _logger;

    public ActivityLogsApiController(IActivityLogService logs, IActivityLogWriter writer, IStringLocalizer<SharedResource> localizer, ILogger<ActivityLogsApiController> logger)
    {
        _logs = logs;
        _writer = writer;
        _L = localizer;
        _logger = logger;
    }

    [HttpGet, RequireSuperAdmin]
    public async Task<IActionResult> Get([FromQuery] string? search, [FromQuery] string? action, [FromQuery] string? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1)
    {
        var result = await _logs.GetAsync(new ActivityLogQuery { Search = search, Action = action, Status = status, FromDay = from, ToDay = to, Page = page });
        return Ok(result);
    }

    /// <summary>What a Clear History request would delete (range as resolved by the server, and the row count). Deletes nothing.</summary>
    [HttpPost("clear/preview"), RequireSuperAdmin]
    public async Task<IActionResult> PreviewClear([FromBody] ClearActivityLogsRequest request)
    {
        var preview = await _logs.PreviewClearAsync(request);
        return preview == null ? BadRequest(new { error = _L["ActLog_Clear_Invalid"].Value }) : Ok(preview);
    }

    /// <summary>Deletes the selected range/type — only after the Super Admin's current password is verified here on the server.</summary>
    [HttpPost("clear"), RequireSuperAdmin, EnableRateLimiting("login")]
    public async Task<IActionResult> Clear([FromBody] ClearActivityLogsRequest request)
    {
        var scopeText = $"range={request.Range}; types={request.Types ?? "all"}" +
            (request.Range == "custom" ? $"; from={request.FromDay:yyyy-MM-dd}; to={request.ToDay:yyyy-MM-dd}" : "");
        var userId = HttpContext.Session.GetUserId();

        if (await _logs.PreviewClearAsync(request) == null)
            return BadRequest(new { error = _L["ActLog_Clear_Invalid"].Value });

        if (userId == null || string.IsNullOrEmpty(request.Password) || !await _logs.VerifyPasswordAsync(userId.Value, request.Password))
        {
            await _writer.LogAsync(new ActivityEntry { Action = ActivityActions.ActivityLogsClear, Success = false, Reason = "incorrect-password", Details = scopeText });
            _logger.LogWarning("AdminAudit {Action} DENIED for admin '{Admin}': incorrect password", ActivityActions.ActivityLogsClear, HttpContext.Session.GetEmail());
            return StatusCode(StatusCodes.Status403Forbidden, new { error = _L["ActLog_Clear_WrongPassword"].Value });
        }

        var outcome = await _logs.ClearAsync(request);
        if (outcome == null) return BadRequest(new { error = _L["ActLog_Clear_Invalid"].Value });

        // Written after the delete, so the record of the clearing itself is never inside what was cleared.
        await _writer.LogAsync(new ActivityEntry { Action = ActivityActions.ActivityLogsClear, Success = true, Details = $"{scopeText}; deleted={outcome.Value.Deleted}" });
        _logger.LogInformation("AdminAudit {Action} by admin '{Admin}': {Scope}; deleted {Deleted}", ActivityActions.ActivityLogsClear, HttpContext.Session.GetEmail(), scopeText, outcome.Value.Deleted);
        return Ok(new { deleted = outcome.Value.Deleted });
    }
}
