namespace MvcApp.Services;

/// <summary>The pages of the User interface (Home area) an Admin can hide, in sidebar order. A page's key also
/// covers its sub-pages (for example Stores covers the store profile).</summary>
public static class UserPages
{
    public sealed record Page(string Key, string Action, string NavLabelKey);

    public static readonly IReadOnlyList<Page> All = new[]
    {
        new Page("workforce", "Workforce", "Nav_Workforce"),
        new Page("workforceplanning", "WorkforcePlanning", "Nav_WorkforcePlanning"),
        new Page("hiringforecast", "HiringForecast", "Nav_HiringForecast"),
        new Page("crewtrainers", "CrewTrainers", "Nav_CrewTrainers"),
        new Page("turnover", "Turnover", "Nav_Turnover"),
        new Page("ninetyday", "NinetyDayTurnover", "Nav_NinetyDay"),
        new Page("comparisons", "Comparisons", "Nav_Comparisons"),
        new Page("retention", "Retention", "Nav_Retention"),
        new Page("exitinterviews", "ExitInterviews", "Nav_ExitInterviews"),
        new Page("earlywarning", "EarlyWarning", "Nav_EarlyWarning"),
        new Page("scorecard", "Scorecard", "Nav_Scorecard"),
        new Page("actioncenter", "ActionCenter", "Nav_ActionCenter"),
        new Page("stores", "Stores", "Nav_Stores"),
        new Page("reports", "Reports", "Nav_Reports"),
    };

    // Controller actions that open a page, mapped to the key that hides them.
    private static readonly Dictionary<string, string> ActionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Workforce"] = "workforce", ["WorkforcePlanning"] = "workforceplanning", ["HiringForecast"] = "hiringforecast",
        ["CrewTrainers"] = "crewtrainers", ["Turnover"] = "turnover", ["NinetyDayTurnover"] = "ninetyday",
        ["Comparisons"] = "comparisons", ["Retention"] = "retention", ["ExitInterviews"] = "exitinterviews",
        ["EarlyWarning"] = "earlywarning", ["Scorecard"] = "scorecard",
        ["ActionCenter"] = "actioncenter", ["ActionCenterDetail"] = "actioncenter", ["ActionPlanGuide"] = "actioncenter",
        ["Stores"] = "stores", ["StoreProfile"] = "stores", ["StoreLeaderProfile"] = "stores",
        ["Reports"] = "reports", ["ReportDetail"] = "reports",
    };

    public static string? KeyOfAction(string? action) =>
        action != null && ActionKeys.TryGetValue(action, out var key) ? key : null;

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);

    /// <summary>The first page that is not hidden (where users land and where a hidden page redirects to).</summary>
    public static Page? FirstVisible(ISet<string> hidden) => All.FirstOrDefault(p => !hidden.Contains(p.Key));
}
