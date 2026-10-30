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
}
