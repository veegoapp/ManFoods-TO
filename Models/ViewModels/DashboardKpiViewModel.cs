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

public class StoreComparisonRow
{
    public string StoreName { get; set; } = "";
    public string OperationConsultant { get; set; } = "";
    public string OperationManager { get; set; } = "";
    public int Headcount { get; set; }
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
}

public class SmartInsightItem
{
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class TrendMatrixRow
{
    public string StoreName            { get; set; } = "";
    public string OperationConsultant  { get; set; } = "";
    public Dictionary<string, double?> PeriodRates { get; set; } = new();
    public double? AvgRate             { get; set; }
}

public class TrendMatrixResult
{
    public List<string>         Periods { get; set; } = new();
    public List<TrendMatrixRow> Rows    { get; set; } = new();
}

public class StoreEmployeeRow
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Gender { get; set; } = "";
    public DateOnly? HireDate { get; set; }
}

public class StoreResignationRow
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Gender { get; set; } = "";
    public DateOnly? HireDate { get; set; }
    public DateOnly? ResignationDate { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
}
