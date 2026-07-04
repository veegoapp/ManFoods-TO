using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class AiUsageService : IAiUsageService
{
    private const string LimitKey = "ai_daily_question_limit";
    private const string InputPriceKey = "ai_input_price_per_million";
    private const string OutputPriceKey = "ai_output_price_per_million";
    private const int DefaultLimit = 30;
    private const double DefaultInputPrice = 0.10;
    private const double DefaultOutputPrice = 0.40;

    private readonly AppDbContext _db;

    public AiUsageService(AppDbContext db) => _db = db;

    public async Task<int> GetDailyLimitAsync()
    {
        var setting = await _db.AppSettings.FindAsync(LimitKey);
        return setting != null && int.TryParse(setting.Value, out var limit) ? limit : DefaultLimit;
    }

    public async Task SetDailyLimitAsync(int limit)
    {
        var setting = await _db.AppSettings.FindAsync(LimitKey);
        var text = limit.ToString();
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = LimitKey, Value = text });
        else setting.Value = text;
        await _db.SaveChangesAsync();
    }

    public async Task<AiCostRates> GetCostRatesAsync()
    {
        var rows = await _db.AppSettings
            .Where(s => s.Key == InputPriceKey || s.Key == OutputPriceKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        double Parse(string key, double fallback) =>
            rows.TryGetValue(key, out var v) && double.TryParse(v, out var d) ? d : fallback;

        return new AiCostRates
        {
            InputPricePerMillion = Parse(InputPriceKey, DefaultInputPrice),
            OutputPricePerMillion = Parse(OutputPriceKey, DefaultOutputPrice),
        };
    }

    public async Task SetCostRatesAsync(double inputPricePerMillion, double outputPricePerMillion)
    {
        async Task Upsert(string key, double value)
        {
            var setting = await _db.AppSettings.FindAsync(key);
            var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (setting == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = text });
            else setting.Value = text;
        }

        await Upsert(InputPriceKey, inputPricePerMillion);
        await Upsert(OutputPriceKey, outputPricePerMillion);
        await _db.SaveChangesAsync();
    }

    public async Task<(int Used, int Limit)> GetUsageAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await _db.AiUsageDaily.FindAsync(userId, today);
        var limit = await GetDailyLimitAsync();
        return (row?.QuestionCount ?? 0, limit);
    }

    public async Task<(bool Allowed, int Used, int Limit)> TryRecordUsageAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var limit = await GetDailyLimitAsync();
        var row = await _db.AiUsageDaily.FindAsync(userId, today);

        if (row == null)
        {
            row = new AiUsageDaily { UserId = userId, UsageDate = today, QuestionCount = 0 };
            _db.AiUsageDaily.Add(row);
        }

        if (row.QuestionCount >= limit) return (false, row.QuestionCount, limit);

        row.QuestionCount++;
        await _db.SaveChangesAsync();
        return (true, row.QuestionCount, limit);
    }

    public async Task RecordTokensAsync(int userId, int promptTokens, int completionTokens)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await _db.AiUsageDaily.FindAsync(userId, today);
        if (row == null)
        {
            row = new AiUsageDaily { UserId = userId, UsageDate = today };
            _db.AiUsageDaily.Add(row);
        }
        row.PromptTokens += promptTokens;
        row.CompletionTokens += completionTokens;
        await _db.SaveChangesAsync();
    }

    public async Task<AiUsageSummary> GetSummaryAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since7 = today.AddDays(-6);
        var since30 = today.AddDays(-29);
        var rates = await GetCostRatesAsync();

        var rows = await _db.AiUsageDaily
            .Where(a => a.UsageDate >= since30)
            .Select(a => new { a.UsageDate, a.QuestionCount, a.PromptTokens, a.CompletionTokens })
            .ToListAsync();

        double Cost(IEnumerable<(long Prompt, long Completion)> items) =>
            items.Sum(i => i.Prompt / 1_000_000.0 * rates.InputPricePerMillion + i.Completion / 1_000_000.0 * rates.OutputPricePerMillion);

        var todayRows = rows.Where(r => r.UsageDate == today).ToList();
        var last7 = rows.Where(r => r.UsageDate >= since7).ToList();
        var last30 = rows;

        return new AiUsageSummary
        {
            QuestionsToday = todayRows.Sum(r => r.QuestionCount),
            QuestionsLast7Days = last7.Sum(r => r.QuestionCount),
            QuestionsLast30Days = last30.Sum(r => r.QuestionCount),
            TokensToday = todayRows.Sum(r => r.PromptTokens + r.CompletionTokens),
            TokensLast30Days = last30.Sum(r => r.PromptTokens + r.CompletionTokens),
            EstimatedCostToday = Math.Round(Cost(todayRows.Select(r => (r.PromptTokens, r.CompletionTokens))), 4),
            EstimatedCostLast30Days = Math.Round(Cost(last30.Select(r => (r.PromptTokens, r.CompletionTokens))), 4),
        };
    }

    public async Task<List<AiUsageTrendPoint>> GetTrendAsync(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-(days - 1));

        var rows = await _db.AiUsageDaily
            .Where(a => a.UsageDate >= since)
            .GroupBy(a => a.UsageDate)
            .Select(g => new { Date = g.Key, Questions = g.Sum(x => x.QuestionCount), Tokens = g.Sum(x => x.PromptTokens + x.CompletionTokens) })
            .ToListAsync();

        var byDate = rows.ToDictionary(r => r.Date);
        var result = new List<AiUsageTrendPoint>();
        for (var d = since; d <= today; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var match);
            result.Add(new AiUsageTrendPoint
            {
                Label = d.ToString("MMM dd"),
                Questions = match?.Questions ?? 0,
                Tokens = match?.Tokens ?? 0,
            });
        }
        return result;
    }

    public async Task<List<AiTopUserRow>> GetTopUsersAsync(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var since = today.AddDays(-(days - 1));
        var rates = await GetCostRatesAsync();

        var perUser = await _db.AiUsageDaily
            .Where(a => a.UsageDate >= since)
            .GroupBy(a => a.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Questions = g.Sum(x => x.QuestionCount),
                PromptTokens = g.Sum(x => x.PromptTokens),
                CompletionTokens = g.Sum(x => x.CompletionTokens),
            })
            .ToListAsync();

        if (perUser.Count == 0) return new List<AiTopUserRow>();

        var userIds = perUser.Select(p => p.UserId).ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

        return perUser
            .Select(p => new AiTopUserRow
            {
                UserId = p.UserId,
                Email = emails.GetValueOrDefault(p.UserId, "—"),
                Questions = p.Questions,
                Tokens = p.PromptTokens + p.CompletionTokens,
                EstimatedCost = Math.Round(p.PromptTokens / 1_000_000.0 * rates.InputPricePerMillion + p.CompletionTokens / 1_000_000.0 * rates.OutputPricePerMillion, 4),
            })
            .OrderByDescending(r => r.Tokens)
            .ToList();
    }

    public async Task ResetUserTodayAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await _db.AiUsageDaily.FindAsync(userId, today);
        if (row != null)
        {
            _db.AiUsageDaily.Remove(row);
            await _db.SaveChangesAsync();
        }
    }
}
