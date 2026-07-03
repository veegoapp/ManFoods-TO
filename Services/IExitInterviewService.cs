using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IExitInterviewService
{
    Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName);
    Task<List<ChartDataItem>> GetReasonsForLeavingAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName);
}
