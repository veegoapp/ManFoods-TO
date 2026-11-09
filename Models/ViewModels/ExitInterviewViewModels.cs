namespace MvcApp.Models.ViewModels;

public class ExitInterviewFilter
{
    public string? Store { get; set; }
    public string? StoreLeader { get; set; }
    public string? OperationConsultant { get; set; }
    public string? OperationManager { get; set; }
    public string? SeniorOperationConsultant { get; set; }
    public string? OperationDirector { get; set; }
    public int? Year { get; set; }
    public string? Months { get; set; }
    public string? Jobs { get; set; }

    /// <summary>Exact store names (server-side only, never bound from the request).
    /// Used by the SOC/OD profile, whose exit interviews aren't snapshotted per person.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public List<string>? StoreNames { get; set; }
}

public class ExitInterviewFilterOptions
{
    public List<string> Stores { get; set; } = new();
    public List<string> StoreLeaders { get; set; } = new();
    public List<string> OperationConsultants { get; set; } = new();
    public List<string> OperationManagers { get; set; } = new();
}

public class EngagementDriverItem
{
    public string Label { get; set; } = "";
    public double PositivePercent { get; set; }
    public int TotalResponses { get; set; }
}

public class ExitSentimentSummary
{
    public double PositivePercent { get; set; }
    public int TotalResponses { get; set; }
}

public class ExitInterviewCommentItem
{
    public string Store { get; set; } = "";
    public string StoreLeader { get; set; } = "";
    public string QuestionLabel { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
}

/// <summary>One month's count of each of the top reasons for leaving, for the
/// Reasons Trend chart.</summary>
public class ExitReasonTrendPoint
{
    public string Label { get; set; } = "";
    public Dictionary<string, int> Counts { get; set; } = new();
}

/// <summary>For one reason for leaving: how many gave it, and what share of
/// them said they'd return — the "Reason vs Would Return" cross-analysis.</summary>
public class ExitReasonReturnItem
{
    public string Reason { get; set; } = "";
    public int Count { get; set; }
    public double WouldReturnPercent { get; set; }
}
