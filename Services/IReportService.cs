using ClosedXML.Excel;

namespace MvcApp.Services;

public interface IReportService
{
    Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildTurnoverReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildNinetyDayReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildRetentionReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildExitInterviewReportAsync(string role, string? assignedName, string? store = null, string? om = null, string? oc = null);
    Task<XLWorkbook> BuildScorecardReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null);
    Task<XLWorkbook> BuildEarlyWarningReportAsync(string role, string? assignedName, string? store = null,
        string? om = null, string? oc = null, string? soc = null, string? od = null,
        string? months = null, int? year = null);
    Task<XLWorkbook> BuildTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null);
    Task<XLWorkbook> BuildNinetyDayTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null);
    Task<XLWorkbook> BuildActionCenterReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildStoresOverviewReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildWorkforceReportAsync(int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null);
    /// <summary>Projected vs actual headcount by job and store for a year: Summary, By Store, By Job,
    /// By Consultant &amp; Manager and the Hiring Plan. The heavy Data sheet and pivots are in
    /// <see cref="BuildWorkforcePlanningDataReportAsync"/>.</summary>
    Task<XLWorkbook> BuildWorkforcePlanningReportAsync(int year, string? months, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);
    /// <summary>The flat Data sheet (one row per store/job/month) and the real Excel pivot tables built on it.</summary>
    Task<XLWorkbook> BuildWorkforcePlanningDataReportAsync(int year, string? months, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);
    /// <summary>Hires needed per month for a year, by store, job, payroll group and responsible person.</summary>
    Task<XLWorkbook> BuildHiringForecastReportAsync(int year, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);
    /// <summary>Crew trainers against the plan and the 1-per-6 rule for one month with a trainer list.</summary>
    Task<XLWorkbook> BuildCrewTrainerReportAsync(int year, int month, string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildOcOmComparisonReportAsync(int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);
    /// <summary>Period A vs Period B comparison — mirrors the Comparisons dashboard
    /// page's own two-sided filter shape (independent year/months/store/OM/OC/SOC/OD
    /// per side). A null side value falls back to that side's dashboard-page default
    /// (A = latest year, all months; B = the same months one year earlier).</summary>
    Task<XLWorkbook> BuildComparisonReportAsync(string role, string? assignedName,
        int? yearA = null, string? monthsA = null, string? storeA = null, string? omA = null, string? ocA = null, string? socA = null, string? odA = null,
        int? yearB = null, string? monthsB = null, string? storeB = null, string? omB = null, string? ocB = null, string? socB = null, string? odB = null);
}
