using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IActivityLogService
{
    /// <summary>True when <paramref name="password"/> is the current password of the account (BCrypt, server-side).</summary>
    Task<bool> VerifyPasswordAsync(int userId, string password);

    Task<ActivityLogPage> GetAsync(ActivityLogQuery query);

    /// <summary>How many rows a Clear History request would delete, with the resolved range. Null = invalid request.</summary>
    Task<ClearActivityLogsPreview?> PreviewClearAsync(ClearActivityLogsRequest request);

    /// <summary>Deletes exactly the rows the request selects (range + type) and nothing else. Null = invalid request.
    /// The caller is responsible for authorization and password verification.</summary>
    Task<(int Deleted, ClearActivityLogsPreview Scope)?> ClearAsync(ClearActivityLogsRequest request);
}
