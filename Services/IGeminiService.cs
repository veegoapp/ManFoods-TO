namespace MvcApp.Services;

public class StoreBreakdown
{
    public string Store { get; set; } = "";
    public int Headcount { get; set; }
    public int Resignations { get; set; }
    public double TurnoverRate { get; set; }
    public int NewHires { get; set; }
}

public class GeminiContext
{
    public int? Month { get; set; }
    public int? Year { get; set; }
    public string? Store { get; set; }

    // Aggregate KPIs
    public int TotalHeadcount { get; set; }
    public int TotalResignations { get; set; }
    public double TurnoverRate { get; set; }
    public int NewHires { get; set; }

    // Per-store breakdown (only populated when viewing all stores)
    public List<StoreBreakdown> StoreBreakdowns { get; set; } = new();

    // Chart breakdowns
    public List<(string Label, int Value)> TurnoverByJobTitle { get; set; } = new();
    public List<(string Label, int Value)> TurnoverByTenure { get; set; } = new();
    public List<(string Label, int Value)> GenderBreakdown { get; set; } = new();

    // Deeper retention context (company-wide, not tied to the selected period/store)
    public List<(int Days, double RetentionRate)> RetentionMilestones { get; set; } = new();
    public List<(string CohortLabel, double Rate, bool IsProvisional)> NinetyDayCohorts { get; set; } = new();
    public List<(string Reason, int Count)> ExitInterviewReasons { get; set; } = new();

    public double? TurnoverRateTarget { get; set; }
    public double? Retention90Target { get; set; }
}

public interface IGeminiService
{
    Task<string> AskAsync(string userQuestion, GeminiContext context);
}
