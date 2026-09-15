namespace MvcApp.Services;

/// <summary>
/// Stable identifiers for the access areas an Admin can independently open or
/// restrict on the Settings page. An "area" maps to a feature/service rather
/// than a literal URL, because several pages share the same backend endpoints
/// (Workforce, Turnover and Comparisons all run on DashboardService, so they
/// share one area). A couple of individual-person sections have their own area
/// so they can be restricted separately from their page's aggregate charts
/// (exit-interview comments, the 90-day early-leaver list).
/// </summary>
public static class AccessAreas
{
    public const string Analytics       = "analytics";        // Workforce + Turnover + Comparisons (DashboardService)
    public const string Retention       = "retention";
    public const string NinetyDay       = "ninety_day";
    public const string NinetyDayLeavers = "ninety_day_leavers"; // sub: named early-leaver list
    public const string ExitInterviews  = "exit_interviews";   // aggregate charts/sentiment/drivers
    public const string ExitComments    = "exit_comments";     // sub: free-text comments
    public const string EarlyWarning    = "early_warning";
    public const string Scorecard       = "scorecard";
    public const string ActionCenter    = "action_center";
    public const string Stores          = "stores";
    public const string Reports         = "reports";

    /// <summary>Not a configurable row — marks the shared filter-dropdown
    /// endpoints (store list, managers, job titles, periods) used by every page.
    /// These widen when ANY area is open so an open page's dropdowns work, and
    /// stay own-stores otherwise. Never appears in the Settings matrix.</summary>
    public const string Shared          = "shared";

    /// <summary>Every configurable area, in display order for the Settings matrix.
    /// "Stores" is intentionally not here: the Stores page reuses the Analytics,
    /// Action Center and Early Warning endpoints, so its visibility follows those
    /// areas rather than a toggle of its own.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Analytics, Retention, NinetyDay, NinetyDayLeavers, ExitInterviews, ExitComments,
        EarlyWarning, Scorecard, ActionCenter, Reports,
    };
}
