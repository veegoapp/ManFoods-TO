using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IWorkforcePlanningService
{
    /// <summary>Projected vs actual headcount by job and store for a year/month,
    /// scoped to the stores the caller may see. <paramref name="stores"/> and
    /// <paramref name="jobs"/> are optional comma-separated filters. Year/month
    /// default to the current period when it has projection data, else the latest.</summary>
    Task<WorkforcePlanningDto> GetAsync(int? year, int? month, string? stores, string? jobs, string role, string? assignedName);
}
