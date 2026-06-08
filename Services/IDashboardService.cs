using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IDashboardService
{
    Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName);
    Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName);
    Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName);
    Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
}
