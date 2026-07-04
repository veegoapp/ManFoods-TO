namespace MvcApp.Models.ViewModels;

public class RetentionMilestoneItem
{
    public int Days { get; set; }
    /// <summary>Human label for this milestone, e.g. "6 Months", "1 Year".</summary>
    public string Label { get; set; } = "";
    public double RetentionRate { get; set; }
    public int TotalHires { get; set; }
    public int Retained { get; set; }
    /// <summary>Label for the most recent cohort month included in this
    /// milestone's calculation — cohorts too recent to have reached the
    /// milestone are excluded rather than shown as provisional.</summary>
    public string ThroughCohortLabel { get; set; } = "";
}

public class SurvivalPoint
{
    public int Day { get; set; }
    public string Label { get; set; } = "";
    public double RetentionRate { get; set; }
    public int SampleSize { get; set; }
}

public class RetentionTrendPoint
{
    public string Label { get; set; } = "";
    /// <summary>Retention rate keyed by milestone label (e.g. "6 Months", "1 Year").</summary>
    public Dictionary<string, double?> Rates { get; set; } = new();
    /// <summary>Whether each milestone (by the same label keys) is still provisional for this cohort.</summary>
    public Dictionary<string, bool> Provisional { get; set; } = new();
}

public class StoreTenureRow
{
    public string StoreName { get; set; } = "";
    public int Headcount { get; set; }
    public List<ChartDataItem> Buckets { get; set; } = new();
}
