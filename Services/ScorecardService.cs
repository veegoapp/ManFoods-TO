using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ScorecardService : IScorecardService
{
    private readonly AppDbContext _db;
    private readonly IExitInterviewService _exitInterviews;

    public ScorecardService(AppDbContext db, IExitInterviewService exitInterviews)
    {
        _db = db;
        _exitInterviews = exitInterviews;
    }

    private static string Pick(StoreReference s, string dimension) => dimension switch
    {
        "leader" => s.StoreLeader,
        "oc" => s.OperationConsultant,
        "om" => s.OperationManager,
        _ => "",
    };

    private async Task<Dictionary<string, string>> BuildStoreToDimensionMapAsync(string dimension, string? om = null, string? oc = null)
    {
        var periods = await _db.StoreReferences.Select(s => new { s.Month, s.Year }).Distinct().ToListAsync();
        if (periods.Count == 0) return new Dictionary<string, string>();
        var latest = periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var rows = await _db.StoreReferences
            .Where(s => s.Month == latest.Month && s.Year == latest.Year)
            .ToListAsync();
        if (!string.IsNullOrEmpty(om)) rows = rows.Where(s => s.OperationManager == om).ToList();
        if (!string.IsNullOrEmpty(oc)) rows = rows.Where(s => s.OperationConsultant == oc).ToList();

        var map = new Dictionary<string, string>();
        foreach (var r in rows)
        {
            var value = Pick(r, dimension);
            if (!string.IsNullOrWhiteSpace(r.StoreName) && !string.IsNullOrWhiteSpace(value))
                map[r.StoreName] = value;
        }
        return map;
    }

    private class HistoricalRecord
    {
        public string Store { get; set; } = "";
        public int? TenureDays { get; set; }
    }

    private async Task<List<HistoricalRecord>> LoadHistoricalRecordsAsync()
    {
        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store })
            .ToListAsync();
        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, HistoricalRecord>();
        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new HistoricalRecord { Store = a.Store, TenureDays = null };
        }
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            byEmployee[r.EmployeeId] = new HistoricalRecord
            {
                Store = r.Store,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }
        return byEmployee.Values.ToList();
    }

    public async Task<List<ScorecardRow>> GetScorecardAsync(string dimension, string? om = null, string? oc = null)
    {
        var storeMap = await BuildStoreToDimensionMapAsync(dimension, om, oc);
        if (storeMap.Count == 0) return new List<ScorecardRow>();

        var periods = await _db.ActiveEmployees.Select(e => new { e.Month, e.Year }).Distinct().ToListAsync();
        (int Month, int Year)? latestPeriod = periods.Count > 0
            ? periods.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Month, p.Year)).First()
            : null;

        var headcountByStore = new Dictionary<string, int>();
        var resignationsByStore = new Dictionary<string, int>();
        if (latestPeriod.HasValue)
        {
            headcountByStore = (await _db.ActiveEmployees
                .Where(e => e.Month == latestPeriod.Value.Month && e.Year == latestPeriod.Value.Year)
                .GroupBy(e => e.Store)
                .Select(g => new { Store = g.Key, Count = g.Count() })
                .ToListAsync()).ToDictionary(x => x.Store, x => x.Count);

            resignationsByStore = (await _db.Resignations
                .Where(r => r.Month == latestPeriod.Value.Month && r.Year == latestPeriod.Value.Year)
                .GroupBy(r => r.Store)
                .Select(g => new { Store = g.Key, Count = g.Count() })
                .ToListAsync()).ToDictionary(x => x.Store, x => x.Count);
        }

        var historical = await LoadHistoricalRecordsAsync();
        var dimensionStores = storeMap.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToHashSet());

        var result = new List<ScorecardRow>();
        // Sequential — EF Core DbContext does not support concurrent queries.
        foreach (var (name, stores) in dimensionStores)
        {
            var headcount = stores.Sum(s => headcountByStore.GetValueOrDefault(s));
            var resignations = stores.Sum(s => resignationsByStore.GetValueOrDefault(s));
            var turnoverRate = headcount > 0 ? Math.Round(resignations * 100.0 / headcount, 1) : 0;

            var records = historical.Where(h => stores.Contains(h.Store)).ToList();
            var total = records.Count;
            var early90 = total > 0 ? Math.Round(records.Count(r => r.TenureDays != null && r.TenureDays <= 90) * 100.0 / total, 1) : 0;
            var retained180 = total > 0 ? Math.Round(records.Count(r => r.TenureDays == null || r.TenureDays > 180) * 100.0 / total, 1) : 0;

            var filter = dimension switch
            {
                "leader" => new ExitInterviewFilter { StoreLeader = name },
                "oc" => new ExitInterviewFilter { OperationConsultant = name },
                "om" => new ExitInterviewFilter { OperationManager = name },
                _ => new ExitInterviewFilter(),
            };
            var sentiment = await _exitInterviews.GetSentimentSummaryAsync(filter, "Admin", null);

            result.Add(new ScorecardRow
            {
                Name = name,
                StoreCount = stores.Count,
                Headcount = headcount,
                TurnoverRate = turnoverRate,
                EarlyLeaver90Rate = early90,
                Retention180Rate = retained180,
                ExitSentimentPercent = sentiment.PositivePercent,
                ExitResponseCount = sentiment.TotalResponses,
            });
        }

        return result.OrderByDescending(r => r.TurnoverRate).ToList();
    }
}
