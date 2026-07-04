using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IDashboardService
{
    Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        string? om = null, string? oc = null);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
    Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName);
    Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null);
    Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null);
    Task<List<StoreEmployeeRow>> GetStoreEmployeesAsync(string store, int? month, int? year);
    Task<List<StoreResignationRow>> GetStoreResignationHistoryAsync(string store);
    Task<List<string>> GetOperationManagersAsync(int? month, int? year);
    Task<List<string>> GetOperationConsultantsAsync(int? month, int? year);
}
