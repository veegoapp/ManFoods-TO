using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IExitInterviewService
{
    Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
    Task<List<ChartDataItem>> GetReasonsForLeavingAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetTrainingAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetFairTreatmentAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWorkPressureReasonAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Combined "would return" + "overall experience" positive-sentiment
    /// rate, for use in leader/consultant/manager scorecards.</summary>
    Task<ExitSentimentSummary> GetSentimentSummaryAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Batched version of GetSentimentSummaryAsync for a whole Scorecard
    /// column at once — one query for every name in "names" instead of one query
    /// per name, avoiding an N+1 when the Scorecard has many leaders/OC/OM. Every
    /// name in "names" is guaranteed a key in the result, defaulting to an empty
    /// summary (TotalResponses=0, PositivePercent=0) when it has no matching exit
    /// interviews — the same shape GetSentimentSummaryAsync would return for it.</summary>
    Task<Dictionary<string, ExitSentimentSummary>> GetSentimentSummariesByDimensionAsync(
        string dimension, IReadOnlyCollection<string> names, string role, string? assignedName);

    Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Count of exit interviews per job title.</summary>
    Task<List<ChartDataItem>> GetByJobTitleAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Monthly count of each of the top reasons for leaving, all-time
    /// (ignores the filter's Year/Months — same "always full history" rule as
    /// other trend charts elsewhere in the app).</summary>
    Task<List<ExitReasonTrendPoint>> GetReasonsTrendAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>For each of the top reasons for leaving, what share of the people
    /// who gave that reason said they'd return to work here again.</summary>
    Task<List<ExitReasonReturnItem>> GetReasonVsWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Per-store engagement-driver negativity, keyed by stable driver
    /// identifiers (HealthKeys.Driver*) rather than localized labels, for the
    /// Store Health engine's Engagement pillar. Each store maps to one
    /// HealthDriverDto per dimension carrying its negative-response % and count;
    /// baseline/points are computed by the caller. Reuses the same access
    /// scoping and sentiment mapping as the rest of this service, so figures
    /// match the Exit Interview page exactly.</summary>
    Task<Dictionary<string, List<HealthDriverDto>>> GetStoreEngagementProfilesAsync(string role, string? assignedName);
}
