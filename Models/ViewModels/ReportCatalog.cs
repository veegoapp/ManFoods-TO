namespace MvcApp.Models.ViewModels;

public class ReportDefinition
{
    public string Id { get; set; } = "";
    public string Section { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string IconBg { get; set; } = "";
    public string IconColor { get; set; } = "";
    /// <summary>Single month + year snapshot filter.</summary>
    public bool UsesPeriod { get; set; }
    /// <summary>Single "since year" filter (cumulative, not a snapshot).</summary>
    public bool UsesYear { get; set; }
    /// <summary>Multi-select store filter.</summary>
    public bool UsesStore { get; set; }
    /// <summary>Multi-select Operation Manager / Operation Consultant filters.</summary>
    public bool UsesOmOc { get; set; }
    /// <summary>Multi-select Months filter, tied to the Year field — narrows which
    /// month columns appear in a trend-matrix-style export.</summary>
    public bool UsesMonths { get; set; }
}

public static class ReportCatalog
{
    public static readonly List<ReportDefinition> All = new()
    {
        new ReportDefinition
        {
            Id = "summary", Section = "Turnover & Workforce", Title = "Monthly Summary",
            Description = "Headcount, New Hires, Resignations, and Turnover Rate for the selected month — broken down by Job Title, Tenure, and Gender.",
            Icon = "bi-file-earmark-spreadsheet-fill", IconBg = "rgba(39,174,96,.12)", IconColor = "#27ae60",
            UsesPeriod = true, UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "stores", Section = "Turnover & Workforce", Title = "Store Comparison",
            Description = "Side-by-side view of all stores for the selected month — Headcount, Hires, Resignations, and Turnover Rate per store.",
            Icon = "bi-shop-window", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesPeriod = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "trend-matrix", Section = "Turnover & Workforce", Title = "Turnover Trend Matrix",
            Description = "Full data table — one row per store, one column per month, showing Turnover % across all available periods from the selected year onward, with an average column.",
            Icon = "bi-table", IconBg = "oklch(0.55 0.15 258 / .10)", IconColor = "oklch(0.5 0.15 258)",
            UsesYear = true, UsesMonths = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "ninety-day", Section = "Deep Analytics", Title = "90-Day Turnover",
            Description = "Cohort trend, full list of early leavers, by-store rates, and aggregated reasons — across all available periods.",
            Icon = "bi-hourglass-split", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "ninety-day-trend-matrix", Section = "Deep Analytics", Title = "90-Day Trend Matrix",
            Description = "Full data table — one row per store, one column per hire-cohort month, showing the 90-day early-leave rate across all available cohorts, with an average column.",
            Icon = "bi-table", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesYear = true, UsesMonths = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "retention", Section = "Deep Analytics", Title = "Retention",
            Description = "Milestone rates (90d–5yr), survival curve, multi-year trend, store leaderboard, and workforce tenure distribution.",
            Icon = "bi-graph-up-arrow", IconBg = "oklch(0.75 0.15 85 / .12)", IconColor = "oklch(0.6 0.13 82)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "scorecard", Section = "Deep Analytics", Title = "Scorecard",
            Description = "KPI rankings for Store Leaders, Operation Consultants, and Operation Managers — Turnover, 90-Day, Retention, and Exit Sentiment.",
            Icon = "bi-award-fill", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "early-warning", Section = "Deep Analytics", Title = "Early Warning",
            Description = "At-risk employee watchlist with ★ risk stars (7 scoring criteria), flagged reasons, hire date, and tenure — scoped to the selected store(s).",
            Icon = "bi-exclamation-diamond-fill", IconBg = "oklch(0.6 0.22 22 / .10)", IconColor = "oklch(0.6 0.22 22)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "exit-interviews", Section = "Exit Interviews", Title = "Exit Interviews Report",
            Description = "Reasons for leaving, engagement drivers, workload ratings, overall experience, and anonymous comments — aggregated across all periods matching the selected filters.",
            Icon = "bi-chat-square-text-fill", IconBg = "oklch(0.65 0.15 190 / .12)", IconColor = "oklch(0.55 0.15 190)",
            UsesStore = true, UsesOmOc = true,
        },
    };

    public static ReportDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}
