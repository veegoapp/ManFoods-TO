namespace MvcApp.Models.ViewModels;

public class DashboardKpiViewModel
{
    public int TotalHeadcount { get; set; }
    public int NewHires { get; set; }
    public int TotalResignations { get; set; }
    public double TurnoverRate { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
}

/// <summary>One month of the company/filter scope: what the Comparisons page draws month by month.</summary>
public class MonthlyPoint
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Headcount { get; set; }
    public int NewHires { get; set; }
    public int Resignations { get; set; }
    /// <summary>Resignations ÷ the month's headcount, in percent.</summary>
    public double TurnoverRate { get; set; }
}

public class ChartDataItem
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
}

public class PeriodItem
{
    public int Month { get; set; }
    public int Year { get; set; }
}

public class StoreBreakdown
{
    public string Store { get; set; } = "";
    public int Headcount { get; set; }
    public int Resignations { get; set; }
    public double TurnoverRate { get; set; }
    public int NewHires { get; set; }
}

public class StoreComparisonRow
{
    public string StoreName { get; set; } = "";
    public string OperationConsultant { get; set; } = "";
    public string OperationManager { get; set; } = "";
    public string SeniorOperationConsultant { get; set; } = "";
    public string OperationDirector { get; set; } = "";
    /// <summary>Displayed headcount — sum across the selected period range.</summary>
    public int Headcount { get; set; }
    /// <summary>Average headcount across the range — the correct denominator
    /// for turnover-rate math, kept separate from the summed display value.</summary>
    public double AvgHeadcount { get; set; }
    public int NewHires { get; set; }
    public int Resignations { get; set; }
    public double TurnoverRate { get; set; }
}

public class OcOmRow
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int StoreCount { get; set; }
    public int TotalHeadcount { get; set; }
    public int TotalResignations { get; set; }
    public double AvgTurnoverRate { get; set; }
}

public class OcOmAnalysisResult
{
    public List<OcOmRow> OcRows { get; set; } = new();
    public List<OcOmRow> OmRows { get; set; } = new();
    public List<OcOmRow> SocRows { get; set; } = new();
    public List<OcOmRow> OdRows { get; set; } = new();
}

public class SmartInsightItem
{
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Which row of the Smart Insights grid this belongs on:
    /// "primary" (highest/best/trend), "leadership" (highest OC/OM/OD
    /// turnover), or "spike" (the up-to-3 stores with a sudden jump).</summary>
    public string Group { get; set; } = "primary";
}

public class TrendMatrixRow
{
    public string StoreName            { get; set; } = "";
    public string OperationConsultant  { get; set; } = "";
    public string OperationManager     { get; set; } = "";
    public Dictionary<string, double?> PeriodRates { get; set; } = new();
    public double? AvgRate             { get; set; }
}

public class TrendMatrixResult
{
    public List<string>         Periods { get; set; } = new();
    public List<TrendMatrixRow> Rows    { get; set; } = new();
}

public class TurnoverTrendResult
{
    public double CurrentRate { get; set; }
    public double? PreviousRate { get; set; }
    public bool HasPrevious { get; set; }
}

public class StoreHeadcountRow
{
    public string StoreName { get; set; } = "";
    public int Headcount { get; set; }
    public Dictionary<string, int> GenderBreakdown { get; set; } = new();
    public Dictionary<string, int> PayrollGroupBreakdown { get; set; } = new();
}

public class StoreLeaderTrackingRow
{
    public string StoreLeader { get; set; } = "";
    public int FromMonth { get; set; }
    public int FromYear { get; set; }
    public int ToMonth { get; set; }
    public int ToYear { get; set; }
    public string FromPeriod { get; set; } = "";
    public string ToPeriod { get; set; } = "";
    public int Months { get; set; }
}

/// <summary>One active-employee row for the Workforce report's detailed sheet.</summary>
public class EmployeeDetailRow
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Grade { get; set; } = "";
    public string PayrollGroup { get; set; } = "";
    public string Gender { get; set; } = "";
    public DateOnly? HireDate { get; set; }
}

/// <summary>One resignation row for the Turnover report's detailed sheet.</summary>
public class ResignationDetailRow
{
    public int Month { get; set; }
    public int Year { get; set; }
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Gender { get; set; } = "";
    public DateOnly? HireDate { get; set; }
    public DateOnly? ResignationDate { get; set; }
}
