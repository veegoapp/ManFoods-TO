namespace MvcApp.Models.ViewModels;

/// <summary>Filters for the Activity Logs page. All optional. FromDay/ToDay are Cairo calendar days (inclusive).</summary>
public class ActivityLogQuery
{
    public string? Search { get; set; }
    /// <summary>One of <see cref="MvcApp.Services.ActivityLogService.ActionFilters"/> keys, or empty.</summary>
    public string? Action { get; set; }
    /// <summary>"", "success" or "failed".</summary>
    public string? Status { get; set; }
    public DateOnly? FromDay { get; set; }
    public DateOnly? ToDay { get; set; }
    public int Page { get; set; } = 1;
}

public class ActivityLogDetail
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>One row of the Activity Logs table. Nothing secret by construction: no such value is ever stored.</summary>
public class ActivityLogItem
{
    public string Key { get; set; } = "";
    /// <summary>UTC instant (ISO 8601).</summary>
    public DateTime Time { get; set; }
    /// <summary>The same instant in Africa/Cairo, "yyyy-MM-dd HH:mm:ss" — what the page shows.</summary>
    public string TimeDisplay { get; set; } = "";
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActionLabel { get; set; } = "";
    public bool Success { get; set; }
    public string ResultLabel { get; set; } = "";
    public string? Target { get; set; }
    public string? Ip { get; set; }
    public string Description { get; set; } = "";
    public List<ActivityLogDetail> Details { get; set; } = new();
}

public class ActivityLogSummary
{
    public int Total { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    /// <summary>Uploads and admin operations (everything that is not a sign-in).</summary>
    public int AdminActions { get; set; }
}

public class ActivityLogPage
{
    public List<ActivityLogItem> Items { get; set; } = new();
    public ActivityLogSummary Summary { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    /// <summary>True when the requested page was beyond the depth the page will read — narrow the filters.</summary>
    public bool Capped { get; set; }
    /// <summary>True when the activity_logs table does not exist yet (migration not applied).</summary>
    public bool Unavailable { get; set; }
}

/// <summary>What "Clear History" should delete: a time range and a type of logs.</summary>
public class ClearActivityLogsRequest
{
    /// <summary>"last-hour", "last-24h", "last-7d", "last-4w", "custom" or "all".</summary>
    public string? Range { get; set; }
    public DateOnly? FromDay { get; set; }
    public DateOnly? ToDay { get; set; }
    /// <summary>"all", "login", "data" or "admin".</summary>
    public string? Types { get; set; }
    /// <summary>The Super Admin's current password — verified on the server, never stored or logged.</summary>
    public string? Password { get; set; }
}

public class ClearActivityLogsPreview
{
    public int Count { get; set; }
    public string? FromDisplay { get; set; }
    public string? ToDisplay { get; set; }
    public string Types { get; set; } = "all";
}
