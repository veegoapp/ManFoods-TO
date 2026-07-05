using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IEarlyWarningService
{
    Task<List<string>> GetStoreListAsync();

    /// <summary>Currently active employees flagged with one or more historical
    /// risk factors (new-hire window, store/role early-leaver history,
    /// approaching a historical peak resignation tenure), highest risk first.
    /// "months"/"year" pick which Active Employees snapshot counts as "currently
    /// active" (defaults to the latest uploaded period) — the historical
    /// baseline behind each risk factor always uses all-time data.</summary>
    Task<List<EarlyWarningItem>> GetWatchlistAsync(string? store, string? months = null, int? year = null);

    Task<EarlyWarningSummary> GetSummaryAsync(string? store, string? months = null, int? year = null);
}
