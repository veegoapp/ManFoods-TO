using ClosedXML.Excel;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ReportService : IReportService
{
    private const string BrandRed = "#DA291C";

    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IExitInterviewService _exitInterviews;
    private readonly IScorecardService _scorecard;
    private readonly IEarlyWarningService _earlyWarning;

    public ReportService(
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IExitInterviewService exitInterviews,
        IScorecardService scorecard,
        IEarlyWarningService earlyWarning)
    {
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _exitInterviews = exitInterviews;
        _scorecard = scorecard;
        _earlyWarning = earlyWarning;
    }

    private static void StyleHeader(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static IXLWorksheet AddSheet(XLWorkbook wb, string name)
    {
        var trimmed = name.Length > 31 ? name[..31] : name;
        return wb.AddWorksheet(trimmed);
    }

    private static void SetNullable(IXLCell cell, double? value)
    {
        if (value.HasValue) cell.Value = value.Value;
        else cell.Value = "—";
    }

    private static void WriteLabelValueSheet(XLWorkbook wb, string sheetName, string labelHeader, string valueHeader, IEnumerable<ChartDataItem> items)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { labelHeader, valueHeader });
        int r = 2;
        foreach (var item in items) { ws.Cell(r, 1).Value = item.Label; ws.Cell(r, 2).Value = item.Value; r++; }
        ws.Columns().AdjustToContents();
    }

    // ── Turnover Summary ────────────────────────────────────
    private async Task AddSummarySheetsAsync(XLWorkbook wb, int month, int year, string role, string? assignedName)
    {
        var kpi = await _dashboard.GetKpisAsync(month, year, null, role, assignedName);
        var jobTitle = await _dashboard.GetTurnoverByJobTitleAsync(month, year, null, role, assignedName);
        var tenure = await _dashboard.GetTurnoverByTenureAsync(month, year, null, role, assignedName);
        var gender = await _dashboard.GetGenderBreakdownAsync(month, year, null, role, assignedName);

        var ws1 = AddSheet(wb, "Summary KPIs");
        StyleHeader(ws1, new[] { "Metric", "Value" });
        ws1.Cell(2, 1).Value = "Total Headcount"; ws1.Cell(2, 2).Value = kpi.TotalHeadcount;
        ws1.Cell(3, 1).Value = "New Hires"; ws1.Cell(3, 2).Value = kpi.NewHires;
        ws1.Cell(4, 1).Value = "Total Resignations"; ws1.Cell(4, 2).Value = kpi.TotalResignations;
        ws1.Cell(5, 1).Value = "Turnover Rate (%)"; ws1.Cell(5, 2).Value = kpi.TurnoverRate;
        ws1.Columns().AdjustToContents();

        WriteLabelValueSheet(wb, "By Job Title", "Job Title", "Resignations", jobTitle);
        WriteLabelValueSheet(wb, "By Tenure", "Tenure Bucket", "Resignations", tenure);
        WriteLabelValueSheet(wb, "By Gender", "Gender", "Count", gender);
    }

    public async Task<XLWorkbook> BuildSummaryReportAsync(int month, int year, string role, string? assignedName)
    {
        var wb = new XLWorkbook();
        await AddSummarySheetsAsync(wb, month, year, role, assignedName);
        return wb;
    }

    // ── Store Comparison ────────────────────────────────────
    private async Task AddStoreComparisonSheetAsync(XLWorkbook wb, int month, int year, string role, string? assignedName)
    {
        var rows = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName);
        var ws = AddSheet(wb, "Store Comparison");
        StyleHeader(ws, new[] { "Store", "OC", "OM", "Headcount", "New Hires", "Resignations", "Turnover %" });
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            ws.Cell(r + 2, 1).Value = row.StoreName;
            ws.Cell(r + 2, 2).Value = row.OperationConsultant;
            ws.Cell(r + 2, 3).Value = row.OperationManager;
            ws.Cell(r + 2, 4).Value = row.Headcount;
            ws.Cell(r + 2, 5).Value = row.NewHires;
            ws.Cell(r + 2, 6).Value = row.Resignations;
            ws.Cell(r + 2, 7).Value = row.TurnoverRate;
        }
        ws.Columns().AdjustToContents();
    }

    public async Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName)
    {
        var wb = new XLWorkbook();
        await AddStoreComparisonSheetAsync(wb, month, year, role, assignedName);
        return wb;
    }

    // ── 90-Day Turnover ─────────────────────────────────────
    private async Task AddNinetyDaySheetsAsync(XLWorkbook wb)
    {
        var periods = await _ninetyDay.GetCohortPeriodsAsync();
        var trend = await _ninetyDay.GetTrendAsync(null);

        var wsTrend = AddSheet(wb, "90D Cohort Trend");
        StyleHeader(wsTrend, new[] { "Cohort", "Total Hires", "Early Leavers", "Rate (%)", "Provisional" });
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = t.Label;
            wsTrend.Cell(i + 2, 2).Value = t.TotalHires;
            wsTrend.Cell(i + 2, 3).Value = t.EarlyLeavers;
            wsTrend.Cell(i + 2, 4).Value = t.Rate;
            wsTrend.Cell(i + 2, 5).Value = t.IsProvisional ? "Yes" : "No";
        }
        wsTrend.Columns().AdjustToContents();

        if (periods.Count > 0)
        {
            var latest = periods[0]; // most recent first, per GetCohortPeriodsAsync contract
            var byStore = await _ninetyDay.GetByStoreAsync(latest.Month, latest.Year);
            WriteLabelValueSheet(wb, $"90D By Store ({latest.Month}-{latest.Year})", "Store", "Early Leave Rate (%)", byStore);
        }

        var wsLeavers = AddSheet(wb, "90D Early Leavers (All)");
        StyleHeader(wsLeavers, new[] { "Cohort", "Name", "Store", "Job Title", "Hire Date", "Resignation Date", "Tenure (days)" });
        int row = 2;
        var reasonTotals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var leavers = await _ninetyDay.GetEarlyLeaversAsync(p.Month, p.Year, null);
            var cohortLabel = $"{p.Month}-{p.Year}";
            foreach (var lv in leavers)
            {
                wsLeavers.Cell(row, 1).Value = cohortLabel;
                wsLeavers.Cell(row, 2).Value = lv.Name;
                wsLeavers.Cell(row, 3).Value = lv.Store;
                wsLeavers.Cell(row, 4).Value = lv.JobTitle;
                wsLeavers.Cell(row, 5).Value = lv.HireDate.ToString("yyyy-MM-dd");
                wsLeavers.Cell(row, 6).Value = lv.ResignationDate.ToString("yyyy-MM-dd");
                wsLeavers.Cell(row, 7).Value = lv.TenureDays;
                row++;
            }

            var reasons = await _ninetyDay.GetEarlyLeaverReasonsAsync(p.Month, p.Year, null);
            foreach (var reason in reasons)
                reasonTotals[reason.Label] = reasonTotals.GetValueOrDefault(reason.Label) + reason.Value;
        }
        wsLeavers.Columns().AdjustToContents();

        WriteLabelValueSheet(wb, "90D Reasons (Aggregated)", "Reason", "Count",
            reasonTotals.OrderByDescending(kv => kv.Value).Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }));
    }

    public async Task<XLWorkbook> BuildNinetyDayReportAsync()
    {
        var wb = new XLWorkbook();
        await AddNinetyDaySheetsAsync(wb);
        return wb;
    }

    // ── Retention ───────────────────────────────────────────
    private async Task AddRetentionSheetsAsync(XLWorkbook wb)
    {
        var milestones = await _retention.GetMilestonesAsync(null);
        var survival = await _retention.GetSurvivalCurveAsync(null);
        var trend = await _retention.GetTrendAsync(null);
        var leaderboard = await _retention.GetStoreLeaderboardAsync();
        var tenureDist = await _retention.GetTenureDistributionAsync(null);
        var insights = await _retention.GetInsightsAsync(null);

        var wsMilestones = AddSheet(wb, "Retention Milestones");
        StyleHeader(wsMilestones, new[] { "Days", "Retention Rate (%)", "Total Hires", "Retained", "Through Cohort" });
        for (int i = 0; i < milestones.Count; i++)
        {
            var m = milestones[i];
            wsMilestones.Cell(i + 2, 1).Value = m.Days;
            wsMilestones.Cell(i + 2, 2).Value = m.RetentionRate;
            wsMilestones.Cell(i + 2, 3).Value = m.TotalHires;
            wsMilestones.Cell(i + 2, 4).Value = m.Retained;
            wsMilestones.Cell(i + 2, 5).Value = m.ThroughCohortLabel;
        }
        wsMilestones.Columns().AdjustToContents();

        var wsSurvival = AddSheet(wb, "Survival Curve");
        StyleHeader(wsSurvival, new[] { "Day", "Retention Rate (%)", "Sample Size" });
        for (int i = 0; i < survival.Count; i++)
        {
            var s = survival[i];
            wsSurvival.Cell(i + 2, 1).Value = s.Day;
            wsSurvival.Cell(i + 2, 2).Value = s.RetentionRate;
            wsSurvival.Cell(i + 2, 3).Value = s.SampleSize;
        }
        wsSurvival.Columns().AdjustToContents();

        var wsTrend = AddSheet(wb, "Retention Trend");
        StyleHeader(wsTrend, new[] { "Cohort", "90-Day (%)", "Provisional", "180-Day (%)", "Provisional", "365-Day (%)", "Provisional" });
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = t.Label;
            SetNullable(wsTrend.Cell(i + 2, 2), t.Retention90);
            wsTrend.Cell(i + 2, 3).Value = t.Provisional90 ? "Yes" : "No";
            SetNullable(wsTrend.Cell(i + 2, 4), t.Retention180);
            wsTrend.Cell(i + 2, 5).Value = t.Provisional180 ? "Yes" : "No";
            SetNullable(wsTrend.Cell(i + 2, 6), t.Retention365);
            wsTrend.Cell(i + 2, 7).Value = t.Provisional365 ? "Yes" : "No";
        }
        wsTrend.Columns().AdjustToContents();

        WriteLabelValueSheet(wb, "Store Leaderboard (180d)", "Store", "Retention Rate (%)", leaderboard);
        WriteLabelValueSheet(wb, "Workforce Tenure", "Tenure Bucket", "Employees", tenureDist);

        var wsInsights = AddSheet(wb, "Retention Insights");
        StyleHeader(wsInsights, new[] { "Insight", "Description" });
        for (int i = 0; i < insights.Count; i++)
        {
            wsInsights.Cell(i + 2, 1).Value = insights[i].Title;
            wsInsights.Cell(i + 2, 2).Value = insights[i].Description;
        }
        wsInsights.Columns().AdjustToContents();
    }

    public async Task<XLWorkbook> BuildRetentionReportAsync()
    {
        var wb = new XLWorkbook();
        await AddRetentionSheetsAsync(wb);
        return wb;
    }

    // ── Exit Interviews (aggregate only — never names or IDs) ──
    private async Task AddExitInterviewSheetsAsync(XLWorkbook wb)
    {
        const string role = "Admin";
        string? assignedName = null;
        var filter = new ExitInterviewFilter();

        var reasons = await _exitInterviews.GetReasonsForLeavingAsync(filter, role, assignedName);
        var wouldReturn = await _exitInterviews.GetWouldReturnAsync(filter, role, assignedName);
        var overallExperience = await _exitInterviews.GetOverallExperienceAsync(filter, role, assignedName);
        var workload = await _exitInterviews.GetWorkloadConditionAsync(filter, role, assignedName);
        var drivers = await _exitInterviews.GetEngagementDriversAsync(filter, role, assignedName);
        var comments = await _exitInterviews.GetCommentsAsync(filter, role, assignedName);

        WriteLabelValueSheet(wb, "EI Reasons for Leaving", "Reason", "Count", reasons);
        WriteLabelValueSheet(wb, "EI Would Return", "Answer", "Count", wouldReturn);
        WriteLabelValueSheet(wb, "EI Overall Experience", "Answer", "Count", overallExperience);
        WriteLabelValueSheet(wb, "EI Workload Condition", "Answer", "Count", workload);

        var wsDrivers = AddSheet(wb, "EI Engagement Drivers");
        StyleHeader(wsDrivers, new[] { "Driver", "Positive (%)", "Total Responses" });
        for (int i = 0; i < drivers.Count; i++)
        {
            wsDrivers.Cell(i + 2, 1).Value = drivers[i].Label;
            wsDrivers.Cell(i + 2, 2).Value = drivers[i].PositivePercent;
            wsDrivers.Cell(i + 2, 3).Value = drivers[i].TotalResponses;
        }
        wsDrivers.Columns().AdjustToContents();

        var wsComments = AddSheet(wb, "EI Comments (Anonymous)");
        StyleHeader(wsComments, new[] { "Store", "Store Leader", "Question", "Comment", "Submitted At" });
        for (int i = 0; i < comments.Count; i++)
        {
            var c = comments[i];
            wsComments.Cell(i + 2, 1).Value = c.Store;
            wsComments.Cell(i + 2, 2).Value = c.StoreLeader;
            wsComments.Cell(i + 2, 3).Value = c.QuestionLabel;
            wsComments.Cell(i + 2, 4).Value = c.Text;
            wsComments.Cell(i + 2, 5).Value = c.SubmittedAt?.ToString("yyyy-MM-dd") ?? "";
        }
        wsComments.Columns().AdjustToContents();
    }

    public async Task<XLWorkbook> BuildExitInterviewReportAsync()
    {
        var wb = new XLWorkbook();
        await AddExitInterviewSheetsAsync(wb);
        return wb;
    }

    // ── Scorecard ───────────────────────────────────────────
    private async Task AddScorecardSheetAsync(XLWorkbook wb, string dimension, string sheetName, string nameHeader)
    {
        var rows = await _scorecard.GetScorecardAsync(dimension);
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { nameHeader, "Stores", "Headcount", "Turnover Rate (%)", "90-Day Early Leave (%)", "180-Day Retention (%)", "Exit Sentiment (%)", "Exit Responses" });
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = r.Name;
            ws.Cell(i + 2, 2).Value = r.StoreCount;
            ws.Cell(i + 2, 3).Value = r.Headcount;
            ws.Cell(i + 2, 4).Value = r.TurnoverRate;
            ws.Cell(i + 2, 5).Value = r.EarlyLeaver90Rate;
            ws.Cell(i + 2, 6).Value = r.Retention180Rate;
            if (r.ExitResponseCount > 0) ws.Cell(i + 2, 7).Value = r.ExitSentimentPercent;
            else ws.Cell(i + 2, 7).Value = "N/A";
            ws.Cell(i + 2, 8).Value = r.ExitResponseCount;
        }
        ws.Columns().AdjustToContents();
    }

    private async Task AddScorecardSheetsAsync(XLWorkbook wb)
    {
        await AddScorecardSheetAsync(wb, "leader", "Scorecard Store Leaders", "Store Leader");
        await AddScorecardSheetAsync(wb, "oc", "Scorecard Op. Consultants", "Operation Consultant");
        await AddScorecardSheetAsync(wb, "om", "Scorecard Op. Managers", "Operation Manager");
    }

    public async Task<XLWorkbook> BuildScorecardReportAsync()
    {
        var wb = new XLWorkbook();
        await AddScorecardSheetsAsync(wb);
        return wb;
    }

    // ── Early Warning ───────────────────────────────────────
    private async Task AddEarlyWarningSheetsAsync(XLWorkbook wb)
    {
        var summary = await _earlyWarning.GetSummaryAsync(null);
        var watchlist = await _earlyWarning.GetWatchlistAsync(null);

        var wsSummary = AddSheet(wb, "Early Warning Summary");
        StyleHeader(wsSummary, new[] { "Metric", "Value" });
        wsSummary.Cell(2, 1).Value = "Total On Watchlist"; wsSummary.Cell(2, 2).Value = summary.TotalWatchlist;
        wsSummary.Cell(3, 1).Value = "High Risk (score 2+)"; wsSummary.Cell(3, 2).Value = summary.HighRiskCount;
        wsSummary.Cell(4, 1).Value = "In First 90 Days"; wsSummary.Cell(4, 2).Value = summary.NewHireWindowCount;
        wsSummary.Cell(5, 1).Value = "Company Baseline Early-Leave Rate (%)"; wsSummary.Cell(5, 2).Value = summary.CompanyBaselineRate;
        wsSummary.Columns().AdjustToContents();

        var wsWatchlist = AddSheet(wb, "Early Warning Watchlist");
        StyleHeader(wsWatchlist, new[] { "Name", "Store", "Job Title", "Hire Date", "Tenure (days)", "Risk Score", "Reasons" });
        for (int i = 0; i < watchlist.Count; i++)
        {
            var w = watchlist[i];
            wsWatchlist.Cell(i + 2, 1).Value = w.Name;
            wsWatchlist.Cell(i + 2, 2).Value = w.Store;
            wsWatchlist.Cell(i + 2, 3).Value = w.JobTitle;
            wsWatchlist.Cell(i + 2, 4).Value = w.HireDate.ToString("yyyy-MM-dd");
            wsWatchlist.Cell(i + 2, 5).Value = w.TenureDays;
            wsWatchlist.Cell(i + 2, 6).Value = w.RiskScore;
            wsWatchlist.Cell(i + 2, 7).Value = string.Join(" | ", w.Reasons);
        }
        wsWatchlist.Columns().AdjustToContents();
    }

    public async Task<XLWorkbook> BuildEarlyWarningReportAsync()
    {
        var wb = new XLWorkbook();
        await AddEarlyWarningSheetsAsync(wb);
        return wb;
    }

    // ── Full Company Report — everything in one workbook ────
    public async Task<XLWorkbook> BuildFullReportAsync(int month, int year, string role, string? assignedName)
    {
        var wb = new XLWorkbook();
        await AddSummarySheetsAsync(wb, month, year, role, assignedName);
        await AddStoreComparisonSheetAsync(wb, month, year, role, assignedName);
        await AddNinetyDaySheetsAsync(wb);
        await AddRetentionSheetsAsync(wb);
        await AddExitInterviewSheetsAsync(wb);
        await AddScorecardSheetsAsync(wb);
        await AddEarlyWarningSheetsAsync(wb);
        return wb;
    }
}
