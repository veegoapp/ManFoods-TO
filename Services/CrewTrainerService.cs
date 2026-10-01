using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class CrewTrainerService : ICrewTrainerService
{
    private const int NewHireDays = 90;
    private const int ResignationLookbackMonths = 6;

    private readonly AppDbContext _db;
    private readonly IWorkforcePlanningService _planning;
    private readonly IMemoryCache _cache;

    public CrewTrainerService(AppDbContext db, IWorkforcePlanningService planning, IMemoryCache cache)
    {
        _db = db;
        _planning = planning;
        _cache = cache;
    }

    // Everything below is cached under the planning change token, so uploading or deleting a trainer list,
    // a roster or a projection refreshes it; nothing is read per filter change.
    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached != null) return cached;
        var value = await load();
        _cache.Set(key, value, WorkforcePlanningService.CacheOptions());
        return value;
    }

    private Task<List<(int Year, int Month)>> GetPeriodsAsync() => CachedAsync("trainers:periods", async () =>
        (await _db.CrewTrainerEmployees.AsNoTracking().Select(t => new { t.Year, t.Month }).Distinct().ToListAsync())
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).Select(p => (p.Year, p.Month)).ToList());

    private sealed record Listed(string EmployeeId, string Name, string Store);

    private Task<List<Listed>> GetListAsync(int year, int month) => CachedAsync($"trainers:list:{year}:{month}", async () =>
        (await _db.CrewTrainerEmployees.AsNoTracking().Where(t => t.Year == year && t.Month == month)
            .Select(t => new Listed(t.EmployeeId, t.Name, t.StoreName)).ToListAsync()));

    // Employees on that month's roster hired in the 90 days up to the end of the month, per store.
    private Task<Dictionary<string, int>> GetNewHiresAsync(int year, int month) => CachedAsync($"trainers:hires:{year}:{month}", async () =>
    {
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var cutoff = end.AddDays(-NewHireDays);
        var rows = await _db.ActiveEmployees.AsNoTracking()
            .Where(e => e.Year == year && e.Month == month && e.HireDate != null && e.HireDate >= cutoff && e.HireDate <= end)
            .GroupBy(e => e.Store).Select(g => new { Store = g.Key, Count = g.Count() }).ToListAsync();
        return rows.ToDictionary(r => (r.Store ?? "").Trim(), r => r.Count, StringComparer.OrdinalIgnoreCase);
    });

    // Distinct trainers (on a list in the window or the month before) who resigned in the last months, with their store.
    private Task<List<(string EmployeeId, string Store)>> GetResignedAsync(int year, int month) => CachedAsync($"trainers:resigned:{year}:{month}", async () =>
    {
        var target = year * 100 + month;
        var periods = (await _db.ActiveEmployees.AsNoTracking().Select(e => e.Year * 100 + e.Month).Distinct().ToListAsync())
            .Where(k => k <= target).OrderByDescending(k => k).ToList();
        var used = periods.Take(ResignationLookbackMonths).ToList();
        var window = periods.Take(ResignationLookbackMonths + 1).ToList();
        if (used.Count == 0) return new List<(string, string)>();
        var rows = await (
            from r in _db.Resignations.AsNoTracking()
            where used.Contains(r.Year * 100 + r.Month)
            join t in _db.CrewTrainerEmployees.AsNoTracking() on r.EmployeeId equals t.EmployeeId
            where window.Contains(t.Year * 100 + t.Month)
            select new { r.EmployeeId, r.Store }).Distinct().ToListAsync();
        return rows.GroupBy(r => r.EmployeeId).Select(g => (g.Key, g.First().Store)).ToList();
    });

    public async Task<CrewTrainerDto> GetAsync(int? year, int? month, string? stores, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var dto = new CrewTrainerDto();
        var periods = await GetPeriodsAsync();
        if (periods.Count == 0) return dto;
        dto.HasData = true;
        dto.Periods = periods.OrderBy(p => p.Year).ThenBy(p => p.Month).Select(p => new PeriodItem { Year = p.Year, Month = p.Month }).ToList();

        var pick = year.HasValue && month.HasValue && periods.Contains((year.Value, month.Value)) ? (year.Value, month.Value) : periods[0];
        dto.Year = pick.Item1; dto.Month = pick.Item2;

        // The plan side: Crew Trainer projected vs actual per store, with the same access and leadership scoping as Workforce Planning.
        var plan = await _planning.GetAsync(dto.Year, dto.Month, stores, string.Join(",", WorkforcePlanningService.CrewTrainerJobs), role, assignedName, om, oc, soc, od);
        dto.Stores = plan.Stores; dto.OperationConsultants = plan.OperationConsultants; dto.OperationManagers = plan.OperationManagers;
        dto.SeniorOperationConsultants = plan.SeniorOperationConsultants; dto.OperationDirectors = plan.OperationDirectors;
        bool samePeriod = plan.HasData && plan.Year == dto.Year && plan.Month == dto.Month;
        dto.HasProjection = samePeriod && plan.ByStore.Any(r => r.Projected > 0);
        var planRows = samePeriod ? plan.ByStore : new List<PlanningRowDto>();
        var universe = new HashSet<string>(planRows.Select(r => r.Name.Trim()), StringComparer.OrdinalIgnoreCase);

        var hires = await GetNewHiresAsync(dto.Year, dto.Month);
        foreach (var r in planRows)
        {
            var newHires = hires.TryGetValue(r.Name.Trim(), out var h) ? h : 0;
            dto.ByStore.Add(new CrewTrainerStoreDto
            {
                Store = r.Name, OperationConsultant = r.OperationConsultant, Projected = r.Projected, Actual = r.Actual, Gap = r.Actual - r.Projected,
                NewHires = newHires, HiresPerTrainer = r.Actual > 0 ? Math.Round(newHires / (double)r.Actual, 1) : null,
            });
        }
        dto.ByStore = dto.ByStore.OrderByDescending(r => r.HiresPerTrainer ?? (r.NewHires > 0 ? double.MaxValue : -1)).ThenBy(r => r.Store, StringComparer.OrdinalIgnoreCase).ToList();

        var totalActual = dto.ByStore.Sum(r => r.Actual);
        var totalHires = dto.ByStore.Sum(r => r.NewHires);
        var resigned = (await GetResignedAsync(dto.Year, dto.Month)).Count(r => universe.Contains((r.Store ?? "").Trim()));
        dto.Kpis = new CrewTrainerKpiDto
        {
            Projected = dto.ByStore.Sum(r => r.Projected), Actual = totalActual, Gap = totalActual - dto.ByStore.Sum(r => r.Projected),
            NewHires = totalHires, HiresPerTrainer = totalActual > 0 ? Math.Round(totalHires / (double)totalActual, 1) : null,
            Resigned = resigned, LookbackMonths = ResignationLookbackMonths,
        };
        dto.Trend = samePeriod ? plan.Trend : new List<PlanningTrendPointDto>();

        // Movement since the previous uploaded list.
        var previous = periods.Where(p => p.Year * 100 + p.Month < dto.Year * 100 + dto.Month).Select(p => ((int Year, int Month)?)p).FirstOrDefault();
        if (previous != null && universe.Count > 0)
        {
            dto.HasPrevious = true; dto.PreviousYear = previous.Value.Year; dto.PreviousMonth = previous.Value.Month;
            bool InScope(Listed l) => universe.Contains((l.Store ?? "").Trim());
            var now = (await GetListAsync(dto.Year, dto.Month)).Where(InScope).ToList();
            var before = (await GetListAsync(previous.Value.Year, previous.Value.Month)).Where(InScope).ToList();
            var nowIds = now.Select(l => l.EmployeeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var beforeIds = before.Select(l => l.EmployeeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            dto.Entered = now.Where(l => !beforeIds.Contains(l.EmployeeId))
                .Select(l => new CrewTrainerMoveDto { EmployeeId = l.EmployeeId, Name = l.Name, Store = l.Store }).OrderBy(m => m.Store).ThenBy(m => m.Name).ToList();
            var left = before.Where(l => !nowIds.Contains(l.EmployeeId)).ToList();
            if (left.Count > 0)
            {
                var ids = left.Select(l => l.EmployeeId).ToList();
                var resignedIds = (await _db.Resignations.AsNoTracking().Where(r => ids.Contains(r.EmployeeId)).Select(r => r.EmployeeId).Distinct().ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var activeIds = (await _db.ActiveEmployees.AsNoTracking().Where(e => e.Year == dto.Year && e.Month == dto.Month && ids.Contains(e.EmployeeId)).Select(e => e.EmployeeId).ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                dto.Left = left.Select(l => new CrewTrainerMoveDto
                {
                    EmployeeId = l.EmployeeId, Name = l.Name, Store = l.Store,
                    Status = resignedIds.Contains(l.EmployeeId) ? "resigned" : activeIds.Contains(l.EmployeeId) ? "active" : "gone",
                }).OrderBy(m => m.Status == "resigned" ? 0 : m.Status == "active" ? 1 : 2).ThenBy(m => m.Store).ThenBy(m => m.Name).ToList();
            }
        }
        return dto;
    }
}
