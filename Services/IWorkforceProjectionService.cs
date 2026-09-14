using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IWorkforceProjectionService
{
    /// <summary>True once any workforce projection row exists at all — lets the
    /// Action Center show the Workforce Outlook pillar as "pending upload"
    /// rather than a healthy zero when no projection has ever been provided.</summary>
    Task<bool> HasAnyAsync();

    /// <summary>The most recent (latest period) projection for each of the given
    /// stores. Stores with no projection are simply absent from the result.</summary>
    Task<Dictionary<string, WorkforceProjectionSnapshot>> GetLatestByStoreAsync(IEnumerable<string> storeNames);
}

/// <summary>The latest projection row for one store, flattened for the engine.</summary>
public class WorkforceProjectionSnapshot
{
    public int Month { get; set; }
    public int Year { get; set; }
    public int ProjectedHeadcount { get; set; }
    public int PlannedHires { get; set; }
}
