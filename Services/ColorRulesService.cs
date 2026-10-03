using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Admin-configurable rate→color thresholds (Settings page), one independent
/// rule set per metric (Turnover Rate, 90-Day Rate, Retention Rate), stored as
/// JSON in the generic app_settings key/value table under "color_rules_{metric}".
/// Every page that colors one of these three rates by threshold reads its rules
/// from here instead of hardcoding its own cutoffs.
/// </summary>
public class ColorRulesService : IColorRulesService
{
    public static readonly string[] Metrics =
    {
        "turnover", "turnover-total",
        "ninety-day", "ninety-day-total",
        "retention", "early-warning"
    };

    private static readonly Dictionary<string, List<ColorRule>> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        // Matches the hardcoded thresholds this feature replaces.
        ["turnover"] = new()
        {
            new ColorRule { UpTo = 4,  Color = "good" },
            new ColorRule { UpTo = 8,  Color = "warning" },
            new ColorRule { UpTo = 15, Color = "warning_strong" },
            new ColorRule { UpTo = null, Color = "bad" },
        },
        ["turnover-total"] = new()
        {
            new ColorRule { UpTo = 10, Color = "good" },
            new ColorRule { UpTo = 20, Color = "warning" },
            new ColorRule { UpTo = 35, Color = "warning_strong" },
            new ColorRule { UpTo = null, Color = "bad" },
        },
        ["ninety-day"] = new()
        {
            new ColorRule { UpTo = 10, Color = "good" },
            new ColorRule { UpTo = 25, Color = "warning" },
            new ColorRule { UpTo = 50, Color = "warning_strong" },
            new ColorRule { UpTo = null, Color = "bad" },
        },
        ["ninety-day-total"] = new()
        {
            new ColorRule { UpTo = 25, Color = "good" },
            new ColorRule { UpTo = 50, Color = "warning" },
            new ColorRule { UpTo = 75, Color = "warning_strong" },
            new ColorRule { UpTo = null, Color = "bad" },
        },
        // Retention had no threshold coloring before this feature — higher is
        // better here, the reverse direction of the other two metrics.
        ["retention"] = new()
        {
            new ColorRule { UpTo = 50, Color = "bad" },
            new ColorRule { UpTo = 70, Color = "warning" },
            new ColorRule { UpTo = null, Color = "good" },
        },
        // Early Warning risk score, 0-5 stars (not a %) — matches the star colors
        // hardcoded on the page before this became configurable: 0-1 green, 2-3
        // yellow, 4-5 red.
        ["early-warning"] = new()
        {
            new ColorRule { UpTo = 1, Color = "good" },
            new ColorRule { UpTo = 3, Color = "warning" },
            new ColorRule { UpTo = null, Color = "bad" },
        },
    };

    private readonly AppDbContext _db;
    public ColorRulesService(AppDbContext db) { _db = db; }

    private static string KeyFor(string metric) => $"color_rules_{metric.ToLower()}";

    public async Task<List<ColorRule>> GetRulesAsync(string metric)
    {
        if (!Defaults.TryGetValue(metric, out var fallback)) return new List<ColorRule>();

        var setting = await _db.AppSettings.FindAsync(KeyFor(metric));
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value)) return fallback;

        try
        {
            var rules = JsonSerializer.Deserialize<List<ColorRule>>(setting.Value);
            return (rules != null && rules.Count > 0) ? rules : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public async Task SaveRulesAsync(string metric, List<ColorRule> rules)
    {
        if (!Defaults.ContainsKey(metric)) throw new ArgumentException($"Unknown metric '{metric}'.", nameof(metric));

        var key = KeyFor(metric);
        var json = JsonSerializer.Serialize(rules);
        var setting = await _db.AppSettings.FindAsync(key);
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = json });
        else setting.Value = json;
        await _db.SaveChangesAsync();
    }
}
