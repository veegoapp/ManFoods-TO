using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IScorecardService
{
    /// <summary>Per-person rollup for the given dimension ("leader", "oc", or
    /// "om"), aggregated across the resolved Year/Months window — properly
    /// following each person across store reassignments within that window —
    /// combined with all-time early-leaver, retention, and exit-interview
    /// sentiment. Ranked worst turnover first.</summary>
    Task<List<ScorecardRow>> GetScorecardAsync(string dimension, string? om = null, string? oc = null, string? months = null, int? year = null);

    /// <summary>Distinct Store Leader names (all-time) for the leader-search combobox.</summary>
    Task<List<string>> GetLeaderNamesAsync();

    /// <summary>One Store Leader's per-period store-assignment history within
    /// the resolved window, in chronological order, with each row marked when
    /// it represents a transfer to a new store.</summary>
    Task<List<LeaderHistoryRow>> GetLeaderHistoryAsync(string leaderName, string? months = null, int? year = null);

    /// <summary>For each Operation Consultant / Operation Manager, how many of
    /// their currently-assigned Store Leaders have above-average turnover
    /// (within the resolved window) — surfaces portfolio-level leadership
    /// problems rather than individual store noise.</summary>
    Task<ScorecardRollupResult> GetRollupAsync(string? months = null, int? year = null);
}
