using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;

namespace MvcApp.Services;

public class NewHireStatusService : INewHireStatusService
{
    // Invalidated by UploadService whenever it writes ActiveEmployees or Resignations.
    public const string RecordsCacheKey = "newhirestatus:records";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;

    public NewHireStatusService(AppDbContext db, IStoreAccessService storeAccess, IMemoryCache cache)
    {
        _db = db;
        _storeAccess = storeAccess;
        _cache = cache;
    }

    // Everyone ever hired, merged from the monthly rosters and the resignation sheets. It has no
    // request-specific parameters, so it is cached whole and every filter is applied in memory.
    private async Task<List<NewHireStatusRow>> LoadAllAsync()
    {
        if (_cache.TryGetValue(RecordsCacheKey, out List<NewHireStatusRow>? cached) && cached != null) return cached;

        var rosterRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Name, e.Store, e.JobTitle, e.PayrollGroup, e.CostCenter, e.Gender, e.HireDate, e.Year, e.Month })
            .ToListAsync();
        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null)
            .Select(r => new { r.EmployeeId, r.Name, r.Store, r.JobTitle, r.PayrollGroup, r.CostCenter, r.Gender, r.HireDate, r.ResignationDate, r.Year, r.Month })
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var latestRoster = rosterRows.Count == 0 ? 0 : rosterRows.Max(r => r.Year * 100 + r.Month);

        // The employee's most recent roster row carries their current store / job / pay group.
        var lastRoster = rosterRows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeId))
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year * 100 + r.Month).First());
        var resignedById = resignationRows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeId))
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year * 100 + r.Month).First());

        string Pick(string? primary, string? fallback) => !string.IsNullOrWhiteSpace(primary) ? primary! : fallback ?? "";

        var records = new List<NewHireStatusRow>();
        foreach (var id in lastRoster.Keys.Union(resignedById.Keys))
        {
            lastRoster.TryGetValue(id, out var a);
            resignedById.TryGetValue(id, out var r);
            var hire = (r?.HireDate ?? a?.HireDate)!.Value;

            var row = new NewHireStatusRow
            {
                EmployeeId = id,
                Name = Pick(r?.Name, a?.Name),
                // The resignation sheet's Store/Job columns are sometimes left blank — fall back to the roster.
                Store = Pick(r?.Store, a?.Store),
                JobTitle = Pick(r?.JobTitle, a?.JobTitle),
                PayrollGroup = Pick(r?.PayrollGroup, a?.PayrollGroup),
                CostCenter = Pick(r?.CostCenter, a?.CostCenter),
                Gender = Pick(r?.Gender, a?.Gender),
                HireDate = hire,
            };

            if (r != null)
            {
                // A resignation record proves the person left, so it wins over any roster row.
                row.Status = NewHireStatuses.Resigned;
                row.ResignationDate = r.ResignationDate;
                row.TenureDays = Math.Max(0, (r.ResignationDate ?? today).DayNumber - hire.DayNumber);
                row.LeftWithin90Days = r.ResignationDate.HasValue && row.TenureDays <= 90;
            }
            else
            {
                row.Status = a!.Year * 100 + a.Month == latestRoster ? NewHireStatuses.Active : NewHireStatuses.NotOnLatestRoster;
                row.TenureDays = Math.Max(0, today.DayNumber - hire.DayNumber);
            }
            records.Add(row);
        }

        _cache.Set(RecordsCacheKey, records, CacheDuration);
        return records;
    }

    public async Task<List<NewHireStatusRow>> GetAsync(string role, string? assignedName, int? year, IReadOnlyCollection<int>? months,
        IReadOnlyCollection<string>? stores, string? status)
    {
        IEnumerable<NewHireStatusRow> rows = await LoadAllAsync();

        if (year.HasValue) rows = rows.Where(r => r.HireDate.Year == year.Value);
        if (months is { Count: > 0 }) rows = rows.Where(r => months.Contains(r.HireDate.Month));
        if (stores is { Count: > 0 }) rows = rows.Where(r => stores.Contains(r.Store));
        if (NewHireStatuses.Normalize(status) is { } wanted) rows = rows.Where(r => r.Status == wanted);

        // Role-based store access always applies on top of the requested filters.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) rows = rows.Where(r => accessible.Contains(r.Store));

        return rows.OrderByDescending(r => r.HireDate).ThenBy(r => r.Store).ThenBy(r => r.Name).ToList();
    }
}
