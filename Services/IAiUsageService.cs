using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IAiUsageService
{
    Task<int> GetDailyLimitAsync();
    Task SetDailyLimitAsync(int limit);

    Task<AiCostRates> GetCostRatesAsync();
    Task SetCostRatesAsync(double inputPricePerMillion, double outputPricePerMillion);

    /// <summary>Current count of questions asked today by this user, and the
    /// active daily limit — does not consume a question.</summary>
    Task<(int Used, int Limit)> GetUsageAsync(int userId);

    /// <summary>Attempts to record one question for this user today. Returns
    /// Allowed=false (without recording) if the daily limit is already hit.</summary>
    Task<(bool Allowed, int Used, int Limit)> TryRecordUsageAsync(int userId);

    /// <summary>Adds the token counts from a completed Gemini call to this
    /// user's tally for today.</summary>
    Task RecordTokensAsync(int userId, int promptTokens, int completionTokens);

    Task<AiUsageSummary> GetSummaryAsync();

    /// <summary>Company-wide daily question/token totals for the last N days.</summary>
    Task<List<AiUsageTrendPoint>> GetTrendAsync(int days);

    /// <summary>Per-user question/token/cost totals over the last N days, highest first.</summary>
    Task<List<AiTopUserRow>> GetTopUsersAsync(int days);

    /// <summary>Zeroes out a user's question count and token tally for today only.</summary>
    Task ResetUserTodayAsync(int userId);
}
