namespace MvcApp.Models.ViewModels;

public class ExitInterviewFilter
{
    public string? Store { get; set; }
    public string? StoreLeader { get; set; }
    public string? OperationConsultant { get; set; }
    public string? OperationManager { get; set; }
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
