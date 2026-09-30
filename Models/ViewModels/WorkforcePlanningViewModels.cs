namespace MvcApp.Models.ViewModels;

/// <summary>Everything the Workforce Planning page needs for one year/month and
/// filter selection: the filter options, headline numbers, the per-job and
/// per-store projected-vs-actual breakdowns, and the year's monthly trend.</summary>
public class WorkforcePlanningDto
{
    /// <summary>False until at least one job projection has been uploaded.</summary>
    public bool HasData { get; set; }
    public List<int> Years { get; set; } = new();
    public int Year { get; set; }
    /// <summary>Months of the selected year that have projection rows.</summary>
    public List<int> Months { get; set; } = new();
    public int Month { get; set; }
    public List<string> Stores { get; set; } = new();
    public List<string> Jobs { get; set; } = new();
    /// <summary>Whether the selected month has an uploaded active-employee roster
    /// (future months only have a projection).</summary>
    public bool HasActual { get; set; }
    /// <summary>How many recent roster months the expected-resignations average is based on
    /// (0 = no history, so hiring need equals the shortage).</summary>
    public int AttritionMonths { get; set; }
    public PlanningKpiDto Kpis { get; set; } = new();
    public List<PlanningRowDto> ByJob { get; set; } = new();
    public List<PlanningRowDto> ByStore { get; set; } = new();
    public List<PlanningTrendPointDto> Trend { get; set; } = new();
}

public class PlanningKpiDto
{
    public int Projected { get; set; }
    public int Actual { get; set; }
    /// <summary>Projected - actual. Positive = shortage, negative = surplus.</summary>
    public int Gap { get; set; }
    public double FillPercent { get; set; }
    public int StoresCount { get; set; }
    public int StoresShort { get; set; }
    /// <summary>Estimated hires needed: per store and job, the shortage plus expected
    /// resignations (never below zero), summed.</summary>
    public int HiringNeed { get; set; }
    /// <summary>Expected resignations per month (recent average) in the compared stores/jobs.</summary>
    public double ExpectedAttrition { get; set; }
}

public class PlanningRowDto
{
    public string Name { get; set; } = "";
    public int Projected { get; set; }
    public int Actual { get; set; }
    public int Gap { get; set; }
    public double FillPercent { get; set; }
    /// <summary>"ok" | "watch" | "critical" | "none" (no actual roster for the month).</summary>
    public string Status { get; set; } = "none";
    public int HiringNeed { get; set; }
    public double ExpectedAttrition { get; set; }
    /// <summary>Operation Consultant of the store (only filled for store rows).</summary>
    public string OperationConsultant { get; set; } = "";
}

public class PlanningTrendPointDto
{
    public int Month { get; set; }
    public int Projected { get; set; }
    /// <summary>Null for months without an uploaded roster.</summary>
    public int? Actual { get; set; }
}

/// <summary>One flat row of the Workforce Planning report: a job in a store in a month.</summary>
public class PlanningDetailRow
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Store { get; set; } = "";
    public string Job { get; set; } = "";
    public int Projected { get; set; }
    /// <summary>Null when the month has no uploaded active-employee roster.</summary>
    public int? Actual { get; set; }
    /// <summary>Expected resignations per month for this store/job (null without a roster).</summary>
    public double? ExpectedAttrition { get; set; }
    /// <summary>max(0, projected − actual + expected resignations); null without a roster.</summary>
    public double? HiringNeed { get; set; }
}

/// <summary>A store's staffing fill for one month — drives the badge on the Stores page cards.</summary>
public class StoreFillDto
{
    public string Store { get; set; } = "";
    public int Projected { get; set; }
    /// <summary>Null when the month has no uploaded active-employee roster.</summary>
    public int? Actual { get; set; }
    public double FillPercent { get; set; }
    /// <summary>"ok" | "watch" | "critical" | "none".</summary>
    public string Status { get; set; } = "none";
}

/// <summary>One store's staffing plan: projected vs actual by job for the month,
/// plus the projected totals for that month and the months after it.</summary>
public class StorePlanDto
{
    public bool HasData { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool HasActual { get; set; }
    public PlanningKpiDto Kpis { get; set; } = new();
    public List<PlanningRowDto> ByJob { get; set; } = new();
    public List<PlanningTrendPointDto> Upcoming { get; set; } = new();
}
