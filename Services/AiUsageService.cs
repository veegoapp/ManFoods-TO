using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AiUsageService : IAiUsageService
{
    private const string LimitKey = "ai_daily_question_limit";
    private const int DefaultLimit = 30;

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
}
