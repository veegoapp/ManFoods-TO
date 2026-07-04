namespace MvcApp.Models.ViewModels;

public class ScorecardRow
{
    public string Name { get; set; } = "";
    public int StoreCount { get; set; }
    public int Headcount { get; set; }
    public double TurnoverRate { get; set; }
    public double EarlyLeaver90Rate { get; set; }
    public double Retention180Rate { get; set; }
    public double ExitSentimentPercent { get; set; }
    public int ExitResponseCount { get; set; }
}

public class LeaderHistoryRow
{
    public string Store { get; set; } = "";
    public int Month { get; set; }
    public int Year { get; set; }
    public string PeriodLabel { get; set; } = "";
    public int Headcount { get; set; }
    public int Resignations { get; set; }
    public double TurnoverRate { get; set; }
    public bool IsStoreTransition { get; set; }
}

public class RollupRow
{
    public string Name { get; set; } = "";
    public int TotalLeaders { get; set; }
    public int FlaggedLeaders { get; set; }
    public double FlaggedPercent { get; set; }
}

public class ScorecardRollupResult
{
    public double AverageTurnoverRate { get; set; }
    public List<RollupRow> ByOperationConsultant { get; set; } = new();
    public List<RollupRow> ByOperationManager { get; set; } = new();
}
