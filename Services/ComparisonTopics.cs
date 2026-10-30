namespace MvcApp.Services;

/// <summary>
/// The topics of the Comparisons page — and of its Excel report, where one topic is one set of sheets.
/// A few topics read data whose service has no store / job-title filter; those filters must be left empty for them
/// (the page and the report both refuse instead of silently comparing unfiltered numbers).
/// </summary>
public static class ComparisonTopics
{
    public const string Turnover = "turnover";
    public const string Breakdown = "breakdown";
    public const string Ninety = "ninety";
    public const string Workforce = "workforce";
    public const string Retention = "retention";
    public const string Scorecard = "scorecard";
    public const string Exit = "exit";
    public const string Drivers = "drivers";

    public static readonly string[] All = { Turnover, Breakdown, Ninety, Workforce, Retention, Scorecard, Exit, Drivers };

    /// <summary>The topic itself when it is a known one, otherwise Turnover (what an old link without a topic gets).</summary>
    public static string Normalize(string? topic) => topic != null && Array.IndexOf(All, topic) >= 0 ? topic : Turnover;

    public static bool IgnoresStore(string topic) => topic == Scorecard;

    public static bool IgnoresJob(string topic) => topic is Ninety or Retention or Scorecard;

    /// <summary>The refusal text when a store / job-title filter is set for a topic that cannot honour it; null when all is well.</summary>
    public static string? UnsupportedFilterMessage(string topic, bool storeSet, bool jobSet)
    {
        var ignored = new List<string>();
        if (storeSet && IgnoresStore(topic)) ignored.Add("Store");
        if (jobSet && IgnoresJob(topic)) ignored.Add("Job Title");
        return ignored.Count == 0 ? null
            : $"The {string.Join(" and ", ignored)} filter does not apply to this topic. Clear it on A and B, or choose another topic.";
    }

    /// <summary>The Reports catalog id of the page a topic's data comes from, when that is not the Comparison page itself.
    /// The report must follow that page's store scope (Settings → Access), as the page's own API does.</summary>
    public static string? SourceReportId(string topic) => topic switch
    {
        Ninety => "ninety-day",
        Retention => "retention",
        Scorecard => "scorecard",
        Exit or Drivers => "exit-interviews",
        _ => null,
    };
}
