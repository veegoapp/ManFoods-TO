using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IActivityLogService
{
    /// <summary>True when <paramref name="password"/> is the current password of the account (BCrypt, server-side).</summary>
    Task<bool> VerifyPasswordAsync(int userId, string password);

    Task<ActivityLogPage> GetAsync(ActivityLogQuery query);

    /// <summary>Failed/blocked sign-ins (24h) and active users (7d). Cached for a minute; cleared when history is cleared.</summary>
    Task<ActivitySecurityOverview> GetSecurityOverviewAsync();

    /// <summary>The rows the given filters select (newest first, at most <see cref="ActivityLogService.MaxExportRows"/>) as an .xlsx file.</summary>
    Task<byte[]> ExportAsync(ActivityLogQuery query);

    /// <summary>How many rows a Clear History request would delete, with the resolved range. Null = invalid request.</summary>
    Task<ClearActivityLogsPreview?> PreviewClearAsync(ClearActivityLogsRequest request);

    /// <summary>Deletes exactly the rows the request selects (range + type) and nothing else. Null = invalid request.
    /// The caller is responsible for authorization and password verification.</summary>
    Task<(int Deleted, ClearActivityLogsPreview Scope)?> ClearAsync(ClearActivityLogsRequest request);
}
