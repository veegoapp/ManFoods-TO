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
    Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName);
}
