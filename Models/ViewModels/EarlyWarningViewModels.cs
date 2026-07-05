namespace MvcApp.Models.ViewModels;

/// <summary>
/// A structured risk factor. "Type" identifies which rule triggered (for
/// charting/counting), "Score" carries the weight of this individual trigger
/// (1-3), and "Params" carries the raw values needed to render a localised
/// sentence client-side.
/// </summary>
public class EarlyWarningReason
{
    public string Type  { get; set; } = "";
    /// <summary>Weight of this single trigger (1-3). Sum across all reasons = RiskScore.</summary>
    public int    Score { get; set; } = 1;
    public Dictionary<string, string> Params { get; set; } = new();
}

public class EarlyWarningItem
{
    public string   Name       { get; set; } = "";
    public string   Store      { get; set; } = "";
    public string   JobTitle   { get; set; } = "";
    public DateOnly HireDate   { get; set; }
    public int      TenureDays { get; set; }
    /// <summary>Cumulative weight across all triggered reasons.</summary>
    public int      RiskScore  { get; set; }
    /// <summary>Normalised 1-5 star display value derived from RiskScore.</summary>
    public int      Stars      { get; set; }
    public List<EarlyWarningReason> Reasons { get; set; } = new();
}

public class EarlyWarningSummary
{
    public int    TotalWatchlist      { get; set; }
    /// <summary>Employees with Stars >= 4.</summary>
    public int    HighRiskCount       { get; set; }
    public int    NewHireWindowCount  { get; set; }
    public double CompanyBaselineRate { get; set; }
}
