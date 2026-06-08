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
