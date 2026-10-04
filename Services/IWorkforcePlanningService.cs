using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IWorkforcePlanningService
{
    /// <summary>Projected vs actual headcount by job and store for a year/month,
    /// scoped to the stores the caller may see. <paramref name="stores"/> and
    /// <paramref name="jobs"/> are optional comma-separated filters. Year/month
    /// default to the current period when it has projection data, else the latest.</summary>
    Task<WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);

    /// <summary>Per-store fill for one exact year/month (empty when that month has no
    /// projection — it never falls back to another period), optionally narrowed to jobs.</summary>
    Task<List<StoreFillDto>> GetStoreFillAsync(int year, int month, string? jobs, string role, string? assignedName);

    /// <summary>One store's plan for an exact year/month: by job, plus this and the next
    /// three projected months. <c>HasData</c> is false when the store has no projection then.</summary>
    Task<StorePlanDto> GetStorePlanAsync(string store, int year, int month, string role, string? assignedName);

    /// <summary>Flat projected-vs-actual rows (one per store, job and month) for the
    /// chosen year, optionally narrowed to <paramref name="months"/> (1-12),
    /// comma-separated store and job filters, and the caller's store access.</summary>
    Task<List<PlanningDetailRow>> GetDetailAsync(int year, IReadOnlyCollection<int>? months, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null);

    /// <summary>Year/month pairs that have projection rows (drives the report's filters).</summary>
    Task<List<PeriodItem>> GetProjectionPeriodsAsync();

    /// <summary>Job titles present in any projection, for the report's job filter.</summary>
    Task<List<string>> GetProjectionJobsAsync();

    /// <summary>Hires needed per store per month for a year. Months with an uploaded roster use the
    /// current hiring-need formula; later months are simulated from the latest roster (expected
    /// resignations leave, hires fill up to the projection). <paramref name="by"/> picks the rows: "store" (default), "job", "payroll" or "consultant".</summary>
    Task<HiringForecastDto> GetHiringForecastAsync(int? year, string? stores, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, string? by = null);
}
