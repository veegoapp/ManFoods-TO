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
    /// <summary>Filter options: the people responsible for the stores the caller can see
    /// that have a projection this year (from the Store Reference file).</summary>
    public List<string> OperationConsultants { get; set; } = new();
    public List<string> OperationManagers { get; set; } = new();
    public List<string> SeniorOperationConsultants { get; set; } = new();
    public List<string> OperationDirectors { get; set; } = new();
    /// <summary>Whether the selected month has an uploaded active-employee roster
    /// (future months only have a projection).</summary>
    public bool HasActual { get; set; }
    /// <summary>How many recent roster months the expected-resignations average is based on
    /// (0 = no history, so hiring need equals the shortage).</summary>
    public int AttritionMonths { get; set; }
    public PlanningKpiDto Kpis { get; set; } = new();
    public List<PlanningRowDto> ByJob { get; set; } = new();
    public List<PlanningRowDto> ByStore { get; set; } = new();
    /// <summary>Projected vs actual per payroll group. A job belongs to one payroll group (taken from
    /// the active-employee roster), so the projection per job is summed into that group. Jobs with no
    /// employee to learn their group from are listed under an empty name ("unassigned").</summary>
    public List<PlanningRowDto> ByPayrollGroup { get; set; } = new();
    /// <summary>The same projected-vs-actual figures rolled up by the people responsible for the stores
    /// (from the Store Reference file), for the compared stores only.</summary>
    public List<PlanningRowDto> ByOperationConsultant { get; set; } = new();
    public List<PlanningRowDto> ByOperationDirector { get; set; } = new();
    public List<PlanningRowDto> ByOperationManager { get; set; } = new();
    public List<PlanningRowDto> BySeniorOperationConsultant { get; set; } = new();
    public List<PlanningTrendPointDto> Trend { get; set; } = new();
}

public class PlanningKpiDto
{
    public int Projected { get; set; }
    public int Actual { get; set; }
    /// <summary>Actual − projected. Positive = surplus (over plan), negative = shortage.</summary>
    public int Gap { get; set; }
    public double FillPercent { get; set; }
    public int StoresCount { get; set; }
    public int StoresShort { get; set; }
    /// <summary>Sum of the real shortages: for each store and job, projected − actual where actual
    /// is below projected (a surplus in one job never offsets a shortage in another).</summary>
    public int Shortage { get; set; }
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
    /// <summary>"ok" (95–100%) | "watch" (85–95%) | "critical" (<85%) | "over" (above 100%) | "none" (no roster).</summary>
    public string Status { get; set; } = "none";
    public int HiringNeed { get; set; }
    public double ExpectedAttrition { get; set; }
    /// <summary>Sum of the store/job shortages in the row (see <see cref="PlanningKpiDto.Shortage"/>).</summary>
    public int Shortage { get; set; }
    /// <summary>Operation Consultant of the store (only filled for store rows).</summary>
    public string OperationConsultant { get; set; } = "";
    /// <summary>Number of stores rolled up into the row (only for the consultant/manager tables).</summary>
    public int StoreCount { get; set; }
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
    /// <summary>The people responsible for the store that month (Store Reference file).</summary>
    public string OperationConsultant { get; set; } = "";
    public string OperationManager { get; set; } = "";
    public string SeniorOperationConsultant { get; set; } = "";
    public string OperationDirector { get; set; } = "";
    /// <summary>The job's payroll group, learned from the active-employee roster ("" = unknown).</summary>
    public string PayrollGroup { get; set; } = "";
}

/// <summary>A store's staffing fill for one month — drives the badge on the Stores page cards.</summary>
public class StoreFillDto
{
    public string Store { get; set; } = "";
    public int Projected { get; set; }
    /// <summary>Null when the month has no uploaded active-employee roster.</summary>
    public int? Actual { get; set; }
    public double FillPercent { get; set; }
    /// <summary>"ok" | "watch" | "critical" | "over" | "none".</summary>
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

/// <summary>One store's hires needed per month for the Hiring Forecast matrix.</summary>
public class HiringForecastRowDto
{
    public string Store { get; set; } = "";
    public string OperationConsultant { get; set; } = "";
    /// <summary>Hires needed in each month, January first (12 values; 0 when the month has no projection).</summary>
    public int[] Months { get; set; } = new int[12];
    public int Total { get; set; }
}

/// <summary>The Hiring Forecast: hires needed per store per month for a year.</summary>
public class HiringForecastDto
{
    public bool HasData { get; set; }
    public int Year { get; set; }
    public List<int> Years { get; set; } = new();
    public List<string> Stores { get; set; } = new();
    public List<string> Jobs { get; set; } = new();
    public List<string> OperationConsultants { get; set; } = new();
    public List<string> OperationManagers { get; set; } = new();
    public List<string> SeniorOperationConsultants { get; set; } = new();
    public List<string> OperationDirectors { get; set; } = new();
    /// <summary>False when no active-employee roster exists to start the forecast from.</summary>
    public bool HasRoster { get; set; }
    /// <summary>What each row is: "store" | "job" | "payroll" | "consultant".</summary>
    public string By { get; set; } = "store";
    /// <summary>Latest roster the forecast starts from (0 when none).</summary>
    public int BaselineYear { get; set; }
    public int BaselineMonth { get; set; }
    /// <summary>Roster months used for the expected-resignation average.</summary>
    public int AttritionMonths { get; set; }
    /// <summary>Company-wide share (%) of hires who leave within 90 days, used to gross hiring up.</summary>
    public double EarlyLeaverRate { get; set; }
    /// <summary>Per month (January first): "actual" (roster exists), "forecast" (simulated) or "none" (no projection).</summary>
    public string[] MonthModes { get; set; } = Enumerable.Repeat("none", 12).ToArray();
    public List<HiringForecastRowDto> Rows { get; set; } = new();
    public int[] MonthTotals { get; set; } = new int[12];
    public int GrandTotal { get; set; }
}
