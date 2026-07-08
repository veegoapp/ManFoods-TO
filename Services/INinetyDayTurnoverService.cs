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

    /// <summary>One row per store with total hires / early leavers / rate for
    /// the resolved cohort months — the 90-day analogue of DashboardService's
    /// GetStoreComparisonAsync.</summary>
    Task<List<NinetyDayStoreRow>> GetStoreComparisonAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Stores grouped by OC / by OM, ranked by average 90-day rate.</summary>
    Task<NinetyDayOcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Reasons for leaving, restricted to early leavers in the given
    /// cohort, joined against exit interviews by employee id (never exposes
    /// the id itself).</summary>
    Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Job titles of early leavers in the resolved cohort months.</summary>
    Task<List<ChartDataItem>> GetEarlyLeaverJobTitlesAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Payroll groups of early leavers in the resolved cohort months.</summary>
    Task<List<ChartDataItem>> GetEarlyLeaverPayrollGroupsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Gender breakdown of early leavers in the resolved cohort months.</summary>
    Task<List<ChartDataItem>> GetEarlyLeaverGenderBreakdownAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>One row per store, one column per hire-cohort month, showing the
    /// 90-day early-leave rate for that cohort — mirrors DashboardService.GetTrendMatrixAsync
    /// but for the 90-day metric instead of overall turnover.</summary>
    Task<TrendMatrixResult> GetTrendMatrixAsync(string? om = null, string? oc = null, string? months = null, int? sinceYear = null);

    /// <summary>Auto-generated narrative insights: recent-vs-prior trend and
    /// best/worst store on the 90-day rate.</summary>
    Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string? store,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? months = null);
}
