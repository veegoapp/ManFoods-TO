using Microsoft.EntityFrameworkCore;
using MvcApp.Data;

namespace MvcApp.Services;

/// <summary>
/// Read access over the optional workforce_projections table for the Store
/// Health engine. Deliberately thin — ingestion lives in UploadService (which
/// already owns file validation, magic-byte checks, and upload-log history),
/// and all scoring/gap math lives in StoreHealthService (which already holds
/// current headcount). This service only surfaces the latest projection per
/// store and whether any projection exists.
/// </summary>
public class WorkforceProjectionService : IWorkforceProjectionService
{
    private readonly AppDbContext _db;

    public WorkforceProjectionService(AppDbContext db) => _db = db;

    public Task<bool> HasAnyAsync() => _db.WorkforceProjections.AnyAsync();

    public async Task<Dictionary<string, WorkforceProjectionSnapshot>> GetLatestByStoreAsync(IEnumerable<string> storeNames)
    {
        var names = storeNames
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return new Dictionary<string, WorkforceProjectionSnapshot>(StringComparer.OrdinalIgnoreCase);

        var rows = await _db.WorkforceProjections
            .Where(w => names.Contains(w.StoreName))
            .ToListAsync();

        return rows
            .GroupBy(w => w.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(w => w.Year).ThenByDescending(w => w.Month).First();
                    return new WorkforceProjectionSnapshot
                    {
                        Month = latest.Month,
                        Year = latest.Year,
                        ProjectedHeadcount = latest.ProjectedHeadcount,
                        PlannedHires = latest.PlannedHires,
                    };
                },
                StringComparer.OrdinalIgnoreCase);
    }
}
