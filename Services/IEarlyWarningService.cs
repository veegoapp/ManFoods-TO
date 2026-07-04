using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IEarlyWarningService
{
    Task<List<string>> GetStoreListAsync();

    /// <summary>Currently active employees flagged with one or more historical
    /// risk factors (new-hire window, store/role early-leaver history,
    /// approaching a historical peak resignation tenure), highest risk first.</summary>
    Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store);

    Task<EarlyWarningSummary> GetSummaryAsync(string? store);
}
