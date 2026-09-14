using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Tests.TestHelpers;

/// <summary>
/// Minimal no-signal stand-ins for StoreActionPlanService's dependencies that are
/// irrelevant to the Fix #2 (bulk detection order) tests. Each method returns the
/// "nothing to see here" result that StoreActionPlanService.ComputeSignalsAsync's
/// own minimum-sample-size guards already treat as a no-op, so only the real
/// DashboardService-driven HIGH_OVERALL_TURNOVER signal can fire in those tests —
/// isolating the streak/order state machine from unrelated signal sources.
/// </summary>
public class NoOpNinetyDayTurnoverService : INinetyDayTurnoverService
{
    public Task<List<PeriodItem>> GetCohortPeriodsAsync() => Task.FromResult(new List<PeriodItem>());
    public Task<List<string>> GetStoreListAsync(string role, string? assignedName) => Task.FromResult(new List<string>());
    public Task<NinetyDayKpiViewModel> GetKpiAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new NinetyDayKpiViewModel());
    public Task<List<RateTrendItem>> GetTrendAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        Task.FromResult(new List<RateTrendItem>());
    public Task<List<ChartDataItem>> GetByStoreAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<NinetyDayStoreRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<NinetyDayStoreRow>());
    public Task<NinetyDayOcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new NinetyDayOcOmAnalysisResult());
    public Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<EarlyLeaverRow>());
    public Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetEarlyLeaverJobTitlesAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetEarlyLeaverPayrollGroupsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetEarlyLeaverGenderBreakdownAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null, string? store = null) =>
        Task.FromResult(new TrendMatrixResult());
    public Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<SmartInsightItem>());
}

public class NoOpRetentionService : IRetentionService
{
    public Task<List<string>> GetStoreListAsync(string role, string? assignedName) => Task.FromResult(new List<string>());
    public Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<RetentionMilestoneItem>());
    public Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<SurvivalPoint>());
    public Task<List<RetentionTrendPoint>> GetTrendAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null) =>
        Task.FromResult(new List<RetentionTrendPoint>());
    public Task<List<ChartDataItem>> GetStoreLeaderboardAsync(string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<StoreTenureRow>> GetTenureDistributionByStoreAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<StoreTenureRow>());
    public Task<List<SurvivalPoint>> GetActiveTenureCurveAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<SurvivalPoint>());
    public Task<List<ChartDataItem>> GetStoreRetentionRankingAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<SmartInsightItem>> GetInsightsAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null) =>
        Task.FromResult(new List<SmartInsightItem>());
    public Task<List<ChartDataItem>> GetRetentionByJobTitleAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetGenderRetentionAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetAverageTenureByStoreAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ManagerTenureRow>> GetAverageTenureByManagerAsync(string dimension, string role, string? assignedName, int? month = null, int? year = null, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        Task.FromResult(new List<ManagerTenureRow>());
    public Task<List<ChartDataItem>> GetTimeToFirstResignationDistributionAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetMonthlyHiringVolumeAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null) =>
        Task.FromResult(new List<ChartDataItem>());
}

public class NoOpEarlyWarningService : IEarlyWarningService
{
    public Task<List<string>> GetStoreListAsync(string role, string? assignedName) => Task.FromResult(new List<string>());
    public Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store, string role, string? assignedName, string? months = null, int? year = null, string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        Task.FromResult(new List<EarlyWarningItem>());
    public Task<EarlyWarningSummary> GetSummaryAsync(string? store, string role, string? assignedName, string? months = null, int? year = null, string? om = null, string? oc = null, string? soc = null, string? od = null) =>
        Task.FromResult(new EarlyWarningSummary());
}

public class NoOpExitInterviewService : IExitInterviewService
{
    public Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName) =>
        Task.FromResult(new ExitInterviewFilterOptions());
    public Task<List<PeriodItem>> GetAvailablePeriodsAsync() => Task.FromResult(new List<PeriodItem>());
    public Task<List<ChartDataItem>> GetReasonsForLeavingAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetTrainingAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetFairTreatmentAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ChartDataItem>> GetWorkPressureReasonAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<EngagementDriverItem>());
    public Task<ExitSentimentSummary> GetSentimentSummaryAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new ExitSentimentSummary());
    public Task<Dictionary<string, ExitSentimentSummary>> GetSentimentSummariesByDimensionAsync(
        string dimension, IReadOnlyCollection<string> names, string role, string? assignedName) =>
        Task.FromResult(names.ToDictionary(n => n, _ => new ExitSentimentSummary()));
    public Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ExitInterviewCommentItem>());
    public Task<List<ChartDataItem>> GetByJobTitleAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ChartDataItem>());
    public Task<List<ExitReasonTrendPoint>> GetReasonsTrendAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ExitReasonTrendPoint>());
    public Task<List<ExitReasonReturnItem>> GetReasonVsWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName) =>
        Task.FromResult(new List<ExitReasonReturnItem>());
    public Task<Dictionary<string, List<HealthDriverDto>>> GetStoreEngagementProfilesAsync(string role, string? assignedName) =>
        Task.FromResult(new Dictionary<string, List<HealthDriverDto>>());
}

public class NoOpStoreService : IStoreService
{
    public Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName) =>
        Task.FromResult(new List<StoreReference>());
}
