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
