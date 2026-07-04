using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IRetentionService
{
    /// <summary>Distinct store names across all hire cohorts, for filter dropdowns.</summary>
    Task<List<string>> GetStoreListAsync();

    /// <summary>Retention rate at 6mo/1/2/3/4/5-year marks since hire, aggregated
    /// across every cohort old enough to have reached that milestone.</summary>
    Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>% of hires still retained at each day mark since hire (0 through 5 years),
    /// aggregated across every cohort old enough to have reached that day.</summary>
    Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Retention rate at every milestone (6mo through 5yr) per hire-month cohort,
    /// chronological order, across all available cohorts (optionally from sinceYear onward) —
    /// unaffected by the discrete cohort-month filter used elsewhere on the page.</summary>
    Task<List<RetentionTrendPoint>> GetTrendAsync(string? store, string? om = null, string? oc = null, int? sinceYear = null);

    /// <summary>1-year retention rate per store, best first, across complete cohorts.</summary>
    Task<List<ChartDataItem>> GetStoreLeaderboardAsync(
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null);

    /// <summary>Tenure buckets for the currently active workforce (latest upload snapshot),
    /// aggregated company-wide (or for the given store/om/oc scope).</summary>
    Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string? om = null, string? oc = null);

    /// <summary>Same tenure buckets as above, broken out per store (latest upload snapshot).</summary>
    Task<List<StoreTenureRow>> GetTenureDistributionByStoreAsync(string? store, string? om = null, string? oc = null);

    Task<List<SmartInsightItem>> GetInsightsAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? months = null);
}
