using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface INinetyDayTurnoverService
{
    /// <summary>Distinct hire-cohort months present in the data (from either
    /// active-employee or resignation hire dates), most recent first.</summary>
    Task<List<PeriodItem>> GetCohortPeriodsAsync();

    /// <summary>Distinct store names across all cohorts, for filter dropdowns.</summary>
    Task<List<string>> GetStoreListAsync();

    Task<NinetyDayKpiViewModel> GetKpiAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Rate per cohort month across all available cohort periods,
    /// chronological order.</summary>
    Task<List<RateTrendItem>> GetTrendAsync(string? store, string? om = null, string? oc = null);

    Task<List<ChartDataItem>> GetByStoreAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Reasons for leaving, restricted to early leavers in the given
    /// cohort, joined against exit interviews by employee id (never exposes
    /// the id itself).</summary>
    Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
}
