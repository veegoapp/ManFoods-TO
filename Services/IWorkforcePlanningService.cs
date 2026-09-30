using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IWorkforcePlanningService
{
    /// <summary>Projected vs actual headcount by job and store for a year/month,
    /// scoped to the stores the caller may see. <paramref name="stores"/> and
    /// <paramref name="jobs"/> are optional comma-separated filters. Year/month
    /// default to the current period when it has projection data, else the latest.</summary>
    Task<WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName);

    /// <summary>Flat projected-vs-actual rows (one per store, job and month) for the
    /// chosen year, optionally narrowed to <paramref name="months"/> (1-12),
    /// comma-separated store and job filters, and the caller's store access.</summary>
    Task<List<PlanningDetailRow>> GetDetailAsync(int year, IReadOnlyCollection<int>? months, string? stores, string? jobs, string role, string? assignedName);

    /// <summary>Year/month pairs that have projection rows (drives the report's filters).</summary>
    Task<List<PeriodItem>> GetProjectionPeriodsAsync();

    /// <summary>Job titles present in any projection, for the report's job filter.</summary>
    Task<List<string>> GetProjectionJobsAsync();
}
