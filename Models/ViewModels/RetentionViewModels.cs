namespace MvcApp.Models.ViewModels;

public class RetentionMilestoneItem
{
    public int Days { get; set; }
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
    public double RetentionRate { get; set; }
    public int SampleSize { get; set; }
}

public class RetentionTrendPoint
{
    public string Label { get; set; } = "";
    public double? Retention90 { get; set; }
    public bool Provisional90 { get; set; }
    public double? Retention180 { get; set; }
    public bool Provisional180 { get; set; }
    public double? Retention365 { get; set; }
    public bool Provisional365 { get; set; }
}
