using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class TargetsService : ITargetsService
{
    private const string TurnoverKey = "target_turnover_rate";
    private const string RetentionKey = "target_retention_90";

    private readonly AppDbContext _db;

    public TargetsService(AppDbContext db) => _db = db;

    private static double? Parse(string? value) =>
        double.TryParse(value, out var d) ? d : null;

    public async Task<TargetsViewModel> GetAsync()
    {
        var rows = await _db.AppSettings
            .Where(s => s.Key == TurnoverKey || s.Key == RetentionKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return new TargetsViewModel
        {
            TurnoverRateTarget = Parse(rows.GetValueOrDefault(TurnoverKey)),
            Retention90Target = Parse(rows.GetValueOrDefault(RetentionKey)),
        };
    }

    public async Task SetAsync(double? turnoverRateTarget, double? retention90Target)
    {
        async Task Upsert(string key, double? value)
        {
            var setting = await _db.AppSettings.FindAsync(key);
            if (value == null)
            {
                if (setting != null) _db.AppSettings.Remove(setting);
                return;
            }
            var text = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (setting == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = text });
            else setting.Value = text;
        }

        await Upsert(TurnoverKey, turnoverRateTarget);
        await Upsert(RetentionKey, retention90Target);
        await _db.SaveChangesAsync();
    }
}
