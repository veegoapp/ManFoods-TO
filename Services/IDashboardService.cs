using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IDashboardService
{
    Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
    Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName);
    Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<TurnoverTrendResult> GetTurnoverTrendAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null, string? jobTitles = null, string? store = null);
    Task<List<string>> GetJobTitlesAsync(int? month, int? year, string role, string? assignedName);

    /// <summary>Headcount, new hires, resignations and turnover for every month of the selected periods — three grouped
    /// queries for the whole range (instead of one KPI request per month). Months with no roster upload are left out.</summary>
    Task<List<MonthlyPoint>> GetMonthlySeriesAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<string>> GetOperationManagersAsync(int? month, int? year, string role, string? assignedName);
    Task<List<string>> GetOperationConsultantsAsync(int? month, int? year, string role, string? assignedName);
    Task<List<string>> GetSeniorOperationConsultantsAsync(int? month, int? year, string role, string? assignedName);
    Task<List<string>> GetOperationDirectorsAsync(int? month, int? year, string role, string? assignedName);

    // Active-workforce composition (as opposed to the Turnover-page methods above,
    // which describe who resigned). Averaged across the resolved fromMonth/fromYear
    // -> month/year range (a single-period average when no "from" is given).
    Task<List<ChartDataItem>> GetHeadcountByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetHeadcountByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetHeadcountByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, string? jobTitles = null);
    Task<List<ChartDataItem>> GetHeadcountTrendAsync(string? store, string role, string? assignedName, string? om, string? oc, string? soc, string? od, int? sinceYear, string? jobTitles = null);
    Task<List<StoreHeadcountRow>> GetStoreHeadcountBreakdownAsync(int month, int year, string role, string? assignedName, string? om, string? oc, string? soc, string? od, string? jobTitles = null);
    Task<List<StoreLeaderTrackingRow>> GetStoreLeaderTrackingAsync(string store, string role, string? assignedName);
    /// <summary>Row-level active-employee listing for the selected month/year —
    /// the detailed sheet on the Workforce report.</summary>
    Task<List<EmployeeDetailRow>> GetEmployeeDetailsAsync(int month, int year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, string? jobTitles = null);
    /// <summary>Row-level resignation listing across every uploaded period —
    /// the detailed sheet on the Turnover report.</summary>
    Task<List<ResignationDetailRow>> GetResignationDetailsAsync(string? store, string role, string? assignedName, string? jobTitles = null);
}
