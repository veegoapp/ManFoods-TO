namespace MvcApp.Models.ViewModels;

public class ReportDefinition
{
    public string Id { get; set; } = "";
    public string Section { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Resx keys for the localized Section/Title/Description shown in the UI.
    /// The plain Section/Title/Description above stay as the English source text.</summary>
    public string SectionKey { get; set; } = "";
    public string TitleKey { get; set; } = "";
    public string DescriptionKey { get; set; } = "";
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
    /// <summary>Two independent Period A / Period B filter panels (year, months,
    /// store, OM/OC/SOC/OD) — the Comparison report's own filter shape, distinct
    /// from every other report's single filter set.</summary>
    public bool UsesComparisonAB { get; set; }
}

public static class ReportCatalog
{
    // Order below is also the display order within each Section on the Reports
    // page (ReportCatalog.All.GroupBy(r => r.Section), in first-seen order).
    public static readonly List<ReportDefinition> All = new()
    {
        // ── Store Operations ──────────────────────────────────
        new ReportDefinition
        {
            Id = "action-center", Section = "Store Operations", Title = "Action Center",
            Description = "Per-store action-plan status — severity, signals, age, ownership, and task progress for every accessible store.",
            SectionKey = "Rep_Section_StoreOperations", TitleKey = "Rep_Title_ActionCenter", DescriptionKey = "Rep_Desc_ActionCenter",
            Icon = "bi-clipboard2-check-fill", IconBg = "oklch(0.6 0.22 22 / .10)", IconColor = "oklch(0.6 0.22 22)",
            UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "stores-overview", Section = "Store Operations", Title = "Stores Overview",
            Description = "Every store's Headcount, Turnover, Action Center status, and Early Warning high-risk count for the selected month — the single cross-page view of store health.",
            SectionKey = "Rep_Section_StoreOperations", TitleKey = "Rep_Title_StoresOverview", DescriptionKey = "Rep_Desc_StoresOverview",
            Icon = "bi-shop", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesPeriod = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "workforce", Section = "Store Operations", Title = "Workforce",
            Description = "Detailed employee-level roster plus active workforce composition for the selected month — Headcount by Job Title, Payroll Group, Tenure, and Gender, Gender Count by Store — and the Headcount Trend over time.",
            SectionKey = "Rep_Section_StoreOperations", TitleKey = "Rep_Title_Workforce", DescriptionKey = "Rep_Desc_Workforce",
            Icon = "bi-people-fill", IconBg = "oklch(0.6 0.13 250 / .10)", IconColor = "oklch(0.5 0.13 250)",
            UsesPeriod = true, UsesStore = true, UsesOmOc = true,
        },

        // ── Turnover ──────────────────────────────────────────
        new ReportDefinition
        {
            Id = "turnover", Section = "Turnover", Title = "Turnover",
            Description = "Company-wide turnover trend across every uploaded period, the latest period broken down by store, the full resignation list, and aggregated breakdowns by job title and tenure.",
            SectionKey = "Rep_Section_Turnover", TitleKey = "Rep_Title_Turnover", DescriptionKey = "Rep_Desc_Turnover",
            Icon = "bi-arrow-down-up", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "trend-matrix", Section = "Turnover", Title = "Turnover Trend Matrix",
            Description = "Full data table — one row per store, one column per month, showing Turnover % across all available periods from the selected year onward, with a total column.",
            SectionKey = "Rep_Section_Turnover", TitleKey = "Rep_Title_TrendMatrix", DescriptionKey = "Rep_Desc_TrendMatrix",
            Icon = "bi-table", IconBg = "oklch(0.55 0.15 258 / .10)", IconColor = "oklch(0.5 0.15 258)",
            UsesYear = true, UsesMonths = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "ninety-day", Section = "Turnover", Title = "90-Day Turnover",
            Description = "Cohort trend, full list of early leavers, by-store rates, and aggregated reasons — across all available periods.",
            SectionKey = "Rep_Section_Turnover", TitleKey = "Rep_Title_NinetyDay", DescriptionKey = "Rep_Desc_NinetyDay",
            Icon = "bi-hourglass-split", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "ninety-day-trend-matrix", Section = "Turnover", Title = "90-Day Trend Matrix",
            Description = "Full data table — one row per store, one column per hire-cohort month, showing the 90-day early-leave rate across all available cohorts, with a total column.",
            SectionKey = "Rep_Section_Turnover", TitleKey = "Rep_Title_NinetyDayTrendMatrix", DescriptionKey = "Rep_Desc_NinetyDayTrendMatrix",
            Icon = "bi-table", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesYear = true, UsesMonths = true, UsesOmOc = true,
        },
        // ── Comparison ────────────────────────────────────────
        new ReportDefinition
        {
            Id = "comparisons", Section = "Comparison", Title = "Comparison",
            Description = "Side-by-side Period A vs Period B — Headcount, New Hires, Resignations, Turnover Rate, and 90-Day Early Leave Rate, company-wide and per store.",
            SectionKey = "Rep_Section_Comparison", TitleKey = "Rep_Title_Comparisons", DescriptionKey = "Rep_Desc_Comparisons",
            Icon = "bi-arrow-left-right", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesComparisonAB = true,
        },
        new ReportDefinition
        {
            Id = "oc-om-comparison", Section = "Comparison", Title = "OC / OM Comparison",
            Description = "Stores, headcount, resignations, and average turnover rate rolled up by Operation Consultant, Operation Manager, Senior Operation Consultant, and Operation Director.",
            SectionKey = "Rep_Section_Comparison", TitleKey = "Rep_Title_OcOmComparison", DescriptionKey = "Rep_Desc_OcOmComparison",
            Icon = "bi-people", IconBg = "oklch(0.55 0.15 258 / .10)", IconColor = "oklch(0.5 0.15 258)",
            UsesPeriod = true, UsesOmOc = true,
        },

        // ── Performance & Risk ────────────────────────────────
        new ReportDefinition
        {
            Id = "retention", Section = "Performance & Risk", Title = "Retention",
            Description = "Milestone rates (90d–5yr), survival curve, multi-year trend, store leaderboard, and workforce tenure distribution.",
            SectionKey = "Rep_Section_PerformanceRisk", TitleKey = "Rep_Title_Retention", DescriptionKey = "Rep_Desc_Retention",
            Icon = "bi-graph-up-arrow", IconBg = "oklch(0.75 0.15 85 / .12)", IconColor = "oklch(0.6 0.13 82)",
            UsesStore = true,
        },
        new ReportDefinition
        {
            Id = "scorecard", Section = "Performance & Risk", Title = "Scorecard",
            Description = "KPI rankings for Store Leaders, Operation Consultants, and Operation Managers — Turnover, 90-Day, Retention, and Exit Sentiment.",
            SectionKey = "Rep_Section_PerformanceRisk", TitleKey = "Rep_Title_Scorecard", DescriptionKey = "Rep_Desc_Scorecard",
            Icon = "bi-award-fill", IconBg = "oklch(0.5 0.18 25 / .10)", IconColor = "oklch(0.5 0.18 25)",
            UsesYear = true, UsesMonths = true, UsesOmOc = true,
        },
        new ReportDefinition
        {
            Id = "early-warning", Section = "Performance & Risk", Title = "Early Warning",
            Description = "At-risk employee watchlist with ★ risk stars (7 scoring criteria), flagged reasons, hire date, and tenure — scoped to the selected store(s).",
            SectionKey = "Rep_Section_PerformanceRisk", TitleKey = "Rep_Title_EarlyWarning", DescriptionKey = "Rep_Desc_EarlyWarning",
            Icon = "bi-exclamation-diamond-fill", IconBg = "oklch(0.6 0.22 22 / .10)", IconColor = "oklch(0.6 0.22 22)",
            UsesYear = true, UsesMonths = true, UsesStore = true, UsesOmOc = true,
        },

        // ── Exit Interviews ───────────────────────────────────
        new ReportDefinition
        {
            Id = "exit-interviews", Section = "Exit Interviews", Title = "Exit Interviews Report",
            Description = "Reasons for leaving, engagement drivers, workload ratings, overall experience, and anonymous comments — aggregated across all periods matching the selected filters.",
            SectionKey = "Rep_Section_ExitInterviews", TitleKey = "Rep_Title_ExitInterviews", DescriptionKey = "Rep_Desc_ExitInterviews",
            Icon = "bi-chat-square-text-fill", IconBg = "oklch(0.65 0.15 190 / .12)", IconColor = "oklch(0.55 0.15 190)",
            UsesStore = true, UsesOmOc = true,
        },
    };

    public static ReportDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}
