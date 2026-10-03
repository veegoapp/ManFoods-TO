using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class CrewTrainerService : ICrewTrainerService
{
    /// <summary>One trainer for every 6 crew-level employees.</summary>
    public const int CrewPerTrainer = 6;
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

    // The jobs marked "crew level" in the Job Payroll Groups list (normalised titles), trainer jobs excluded:
    // the people one trainer trains.
    private Task<HashSet<string>> GetCrewLevelJobsAsync() => CachedAsync("trainers:crew-level-jobs", async () =>
    {
        var trainerJobs = WorkforcePlanningService.CrewTrainerJobs.Select(WorkforcePlanningService.Norm).ToHashSet();
        var jobs = await _db.JobPayrollGroups.AsNoTracking().Where(j => j.IsCrewLevel).Select(j => j.JobTitle).ToListAsync();
        return jobs.Select(WorkforcePlanningService.Norm).Where(j => j.Length > 0 && !trainerJobs.Contains(j)).ToHashSet();
    });

    /// <summary>Trainers the rule asks for: crew level ÷ 6 rounded to the nearest whole number, half up.</summary>
    internal static int RequiredTrainers(int crewLevel) => (int)Math.Floor(crewLevel / (double)CrewPerTrainer + 0.5);

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

    /// <summary>Adds the store rows up per leader (blank leaders skipped), most short of trainers first.</summary>
    private static List<CrewTrainerGroupDto> RollUp(List<CrewTrainerStoreDto> stores, Func<CrewTrainerStoreDto, string> leader) =>
        stores.Where(r => !string.IsNullOrWhiteSpace(leader(r)))
            .GroupBy(r => leader(r).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CrewTrainerGroupDto
            {
                Name = g.Key, StoreCount = g.Count(), Projected = g.Sum(r => r.Projected), Actual = g.Sum(r => r.Actual), Gap = g.Sum(r => r.Gap),
                CrewLevel = g.Sum(r => r.CrewLevel), Required = g.Sum(r => r.Required), GapRule = g.Sum(r => r.GapRule),
            })
            .OrderBy(r => r.GapRule).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

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

        // One row per store from the month's projected-vs-actual rows: the trainer jobs give projected and actual
        // trainers, the crew-level jobs (trainers excluded: they are moved into the trainer jobs) give the people the
        // rule counts. Same access and leadership scoping as Workforce Planning.
        var crewJobs = await GetCrewLevelJobsAsync();
        dto.HasCrewLevelJobs = crewJobs.Count > 0;
        dto.CrewPerTrainer = CrewPerTrainer;
        var trainerJobNames = WorkforcePlanningService.CrewTrainerJobs.Select(WorkforcePlanningService.Norm).ToHashSet();
        var detail = samePeriod
            ? await _planning.GetDetailAsync(dto.Year, new[] { dto.Month }, stores, null, role, assignedName, om, oc, soc, od)
            : new List<PlanningDetailRow>();
        var byStore = new Dictionary<string, CrewTrainerStoreDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in detail)
        {
            var job = WorkforcePlanningService.Norm(r.Job);
            var isTrainer = trainerJobNames.Contains(job);
            if (!isTrainer && !crewJobs.Contains(job)) continue;
            var key = r.Store.Trim();
            if (!byStore.TryGetValue(key, out var row)) byStore[key] = row = new CrewTrainerStoreDto
            {
                Store = r.Store, OperationConsultant = r.OperationConsultant, OperationManager = r.OperationManager,
                SeniorOperationConsultant = r.SeniorOperationConsultant, OperationDirector = r.OperationDirector,
            };
            if (isTrainer) { row.Projected += r.Projected; row.Actual += r.Actual ?? 0; }
            else row.CrewLevel += r.Actual ?? 0;
        }
        foreach (var row in byStore.Values)
        {
            row.Gap = row.Actual - row.Projected;
            row.Required = RequiredTrainers(row.CrewLevel);
            row.GapRule = row.Actual - row.Required;
        }
        dto.ByStore = byStore.Values.OrderBy(r => r.GapRule).ThenBy(r => r.Store, StringComparer.OrdinalIgnoreCase).ToList(); // most short of trainers first
        dto.HasProjection = dto.ByStore.Any(r => r.Projected > 0);
        dto.ByOperationConsultant = RollUp(dto.ByStore, r => r.OperationConsultant);
        dto.BySeniorOperationConsultant = RollUp(dto.ByStore, r => r.SeniorOperationConsultant);
        dto.ByOperationDirector = RollUp(dto.ByStore, r => r.OperationDirector);
        dto.ByOperationManager = RollUp(dto.ByStore, r => r.OperationManager);
        var universe = new HashSet<string>(dto.ByStore.Select(r => r.Store.Trim()), StringComparer.OrdinalIgnoreCase);

        var resigned = (await GetResignedAsync(dto.Year, dto.Month)).Count(r => universe.Contains((r.Store ?? "").Trim()));
        dto.Kpis = new CrewTrainerKpiDto
        {
            Projected = dto.ByStore.Sum(r => r.Projected), Actual = dto.ByStore.Sum(r => r.Actual), Gap = dto.ByStore.Sum(r => r.Gap),
            CrewLevel = dto.ByStore.Sum(r => r.CrewLevel), Required = dto.ByStore.Sum(r => r.Required), GapRule = dto.ByStore.Sum(r => r.GapRule),
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
