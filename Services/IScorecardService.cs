using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IScorecardService
{
    /// <summary>Per-person rollup for the given dimension ("leader", "oc", or
    /// "om"), combining latest-period turnover with all-time early-leaver,
    /// retention, and exit-interview sentiment — ranked worst turnover first.</summary>
    Task<List<ScorecardRow>> GetScorecardAsync(string dimension, string? om = null, string? oc = null);
}
