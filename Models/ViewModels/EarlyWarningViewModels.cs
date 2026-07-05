namespace MvcApp.Models.ViewModels;

/// <summary>
/// A structured risk factor — "Type" identifies which rule triggered (for
/// charting/counting) and "Params" carries the raw values needed to render
/// a localized sentence client-side, instead of baking pre-rendered text
/// (in one language) into the API response.
/// </summary>
public class EarlyWarningReason
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new();
}

public class EarlyWarningItem
{
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public DateOnly HireDate { get; set; }
    public int TenureDays { get; set; }
    public int RiskScore { get; set; }
    public List<EarlyWarningReason> Reasons { get; set; } = new();
}

public class EarlyWarningSummary
{
    public int TotalWatchlist { get; set; }
    public int HighRiskCount { get; set; }
    public int NewHireWindowCount { get; set; }
    public double CompanyBaselineRate { get; set; }
}
