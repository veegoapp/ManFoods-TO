namespace MvcApp.Services;

public interface IAiUsageService
{
    Task<int> GetDailyLimitAsync();
    Task SetDailyLimitAsync(int limit);

    /// <summary>Current count of questions asked today by this user, and the
    /// active daily limit — does not consume a question.</summary>
    Task<(int Used, int Limit)> GetUsageAsync(int userId);

    /// <summary>Attempts to record one question for this user today. Returns
    /// Allowed=false (without recording) if the daily limit is already hit.</summary>
    Task<(bool Allowed, int Used, int Limit)> TryRecordUsageAsync(int userId);
}
