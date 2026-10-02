using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Extensions;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>Everything a log entry may carry. There is deliberately no field for a password, hash, OTP code,
/// recovery key or token — callers cannot hand one over.</summary>
public sealed class ActivityEntry
{
    public required string Action { get; init; }
    public bool Success { get; init; } = true;
    /// <summary>Who did it. Null = the signed-in user of the current request (admin actions).</summary>
    public string? UserEmail { get; init; }
    public int? ActorUserId { get; init; }
    public int? TargetUserId { get; init; }
    public string? Portal { get; init; }
    public string? Reason { get; init; }
    public string? Details { get; init; }
    public string? UserAgent { get; init; }
}

public interface IActivityLogWriter
{
    /// <summary>Records one activity. Never throws: a logging failure (for example the table not being migrated yet)
    /// must not break the login, upload or admin action it describes.</summary>
    Task LogAsync(ActivityEntry entry);
}

public class ActivityLogWriter : IActivityLogWriter
{
    // Column sizes (see scripts/migrate.sql): anything longer is clipped, so a hostile value cannot bloat a row.
    private const int EmailMax = 256, ActionMax = 40, PortalMax = 20, ReasonMax = 100, DetailsMax = 1000, UserAgentMax = 300, IpMax = 64;

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ActivityLogWriter> _logger;

    public ActivityLogWriter(AppDbContext db, IHttpContextAccessor http, ILogger<ActivityLogWriter> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task LogAsync(ActivityEntry e)
    {
        ActivityLog? row = null;
        try
        {
            var ctx = _http.HttpContext;
            var session = ctx?.Session;
            var email = e.UserEmail ?? SafeSession(() => session?.GetEmail());
            row = new ActivityLog
            {
                OccurredAt = DateTime.UtcNow,
                Category = ActivityActions.CategoryOf(e.Action),
                Action = Clip(e.Action, ActionMax)!,
                Success = e.Success,
                UserEmail = Clean(email, EmailMax),
                ActorUserId = e.ActorUserId ?? SafeSession(() => session?.GetUserId()),
                TargetUserId = e.TargetUserId,
                IpAddress = Clip(ctx?.Connection.RemoteIpAddress?.ToString(), IpMax),
                UserAgent = Clean(e.UserAgent, UserAgentMax),
                Portal = Clip(e.Portal, PortalMax),
                Reason = Clip(e.Reason, ReasonMax),
                Details = Clean(e.Details, DetailsMax),
            };
            _db.ActivityLogs.Add(row);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Detach the failed row so it cannot be retried by (and break) a later SaveChanges on this request's context.
            if (row != null) _db.Entry(row).State = EntityState.Detached;
            _logger.LogWarning(ex, "Could not write the activity log entry '{Action}'.", e.Action);
        }
    }

    private static T? SafeSession<T>(Func<T?> read) { try { return read(); } catch { return default; } }

    private static string? Clip(string? s, int max) => string.IsNullOrEmpty(s) ? null : (s.Length <= max ? s : s[..max]);

    /// <summary>Clipped, with control characters (newlines, tabs...) removed so a value cannot forge or hide a line.</summary>
    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = new string(s.Trim().Where(c => !char.IsControl(c)).ToArray());
        return Clip(cleaned, max);
    }
}
