using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// The Action Center's analytical engine. Scores every accessible store on a
/// weighted, multi-pillar Store Health Index computed by statistical deviation
/// from the company-wide distribution (not fixed thresholds), merges the
/// live plan-lifecycle state, and derives the ranked weighted action plan and
/// forward workforce outlook. Read-only: the plan write lifecycle (detection,
/// notes, assignment, close) stays in StoreActionPlanService.
/// </summary>
public interface IStoreHealthService
{
    /// <summary>Every accessible store with its full health assessment, ordered
    /// by impact-weighted priority (worst first).</summary>
    Task<List<StoreHealthRowDto>> GetStoreHealthRowsAsync(string role, string? email);

    /// <summary>Company-wide Action Center summary: health-tier counts, pillar
    /// impact, company engagement-driver breakdown, plan-lifecycle counts, and
    /// the top-priority stores.</summary>
    Task<StoreHealthSummaryDto> GetSummaryAsync(string role, string? email);

    /// <summary>One store's health breakdown plus its ranked weighted action
    /// plan and workforce outlook. Null when the role/email can't access it.</summary>
    Task<StoreHealthDetailDto?> GetDetailAsync(string storeName, string role, string? email);
}
