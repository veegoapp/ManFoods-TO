namespace MvcApp.Models.ViewModels;

public class EarlyWarningItem
{
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public int TenureDays { get; set; }
    public int RiskScore { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class EarlyWarningSummary
{
    public int TotalWatchlist { get; set; }
    public int HighRiskCount { get; set; }
    public int NewHireWindowCount { get; set; }
    public double CompanyBaselineRate { get; set; }
}
