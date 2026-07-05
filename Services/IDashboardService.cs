using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IDashboardService
{
    Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
    Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName);
    Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
    Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, int? sinceYear = null);
    Task<List<string>> GetOperationManagersAsync(int? month, int? year);
    Task<List<string>> GetOperationConsultantsAsync(int? month, int? year);

    // Active-workforce composition (as opposed to the Turnover-page methods above,
    // which describe who resigned). All are a snapshot of the given month/year.
    Task<List<ChartDataItem>> GetHeadcountByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetHeadcountByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetHeadcountByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetHeadcountTrendAsync(string? store, string? om, string? oc, int? sinceYear);
    Task<List<StoreHeadcountRow>> GetStoreHeadcountBreakdownAsync(int month, int year, string? om, string? oc);
}
