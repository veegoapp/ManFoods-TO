using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IRetentionService
{
    /// <summary>Distinct store names across all hire cohorts, for filter dropdowns.</summary>
    Task<List<string>> GetStoreListAsync();

    /// <summary>Retention rate at 30/90/180/365 days since hire, aggregated across
    /// every cohort old enough to have reached that milestone.</summary>
    Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null);

    /// <summary>% of hires still retained at each day mark since hire (0 through 365),
    /// aggregated across every cohort old enough to have reached that day.</summary>
    Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null);

    /// <summary>90/180/365-day retention rate per hire-month cohort, chronological order.</summary>
    Task<List<RetentionTrendPoint>> GetTrendAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null);

    /// <summary>180-day retention rate per store, best first, across complete cohorts.</summary>
    Task<List<ChartDataItem>> GetStoreLeaderboardAsync(
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null);

    /// <summary>Tenure buckets for the currently active workforce (latest upload snapshot).</summary>
    Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string? om = null, string? oc = null);

    Task<List<SmartInsightItem>> GetInsightsAsync(string? store,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null);
}
