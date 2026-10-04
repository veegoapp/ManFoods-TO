using ClosedXML.Excel;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ReportService : IReportService
{
    private const string BrandRed = "#DA291C";
    private const string BandFill = "#FAFAFA";
    private const string GridColor = "#D9D9D9";
    private const string PercentFormat = "0.0%";
    // DateOnly does not support the DateTime "mm" minute token. Use "MM" for
    // month so both DateOnly.ToString and Excel's date format are valid.
    private const string DateFormat = "yyyy-MM-dd";

    // Formula-injection guard (CWE-1236): any free-text value written into a
    // workbook cell — employee/leader/OC/OM names, store names, comments,
    // job titles — must not be allowed to open as a formula in Excel. A
    // leading '=', '+', '-', '@', tab, or CR is how that's triggered, so
    // prefixing those with an apostrophe forces the cell to stay plain text.
    // The single place this logic lives; every text cell below with
    // database/imported content goes through it. Never applied to numeric,
    // date, or the app's own fixed status/enum strings.
    private static readonly char[] FormulaTriggerChars = { '=', '+', '-', '@', '\t', '\r' };
    private const int ExcelCellTextLimit = 32767;

    private static string SafeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var text = value.Length > ExcelCellTextLimit ? value[..ExcelCellTextLimit] : value;
        if (!FormulaTriggerChars.Contains(text[0])) return text;
        return "'" + (text.Length >= ExcelCellTextLimit ? text[..(ExcelCellTextLimit - 1)] : text);
    }

    private static string StoreKey(string? storeName) => storeName?.Trim() ?? "";

    /// <summary>Column order requested for every store-scoped report row:
    /// Head Manager, Operation Consultant, Senior Operation Consultant,
    /// Operation Manager, Operation Director.</summary>
    private static readonly string[] LeadershipHeaders =
        { "Head Manager", "Operation Consultant", "Senior Operation Consultant", "Operation Manager", "Operation Director" };

    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IExitInterviewService _exitInterviews;
    private readonly IScorecardService _scorecard;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly IStoreActionPlanService _actionPlans;
    private readonly IStoreService _stores;
    private readonly IAccessAreaContext _areaContext;
    private readonly IWorkforcePlanningService _planning;
    private readonly ICrewTrainerService? _trainers;

    public ReportService(
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IExitInterviewService exitInterviews,
        IScorecardService scorecard,
        IEarlyWarningService earlyWarning,
        IStoreActionPlanService actionPlans,
        IStoreService stores,
        IAccessAreaContext areaContext,
        IWorkforcePlanningService planning,
        ICrewTrainerService? trainers = null)
    {
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _exitInterviews = exitInterviews;
        _scorecard = scorecard;
        _earlyWarning = earlyWarning;
        _actionPlans = actionPlans;
        _stores = stores;
        _areaContext = areaContext;
        _planning = planning;
        _trainers = trainers;
    }

    /// <summary>Runs a fetch under a specific access area instead of whatever the
    /// current export endpoint is tagged with. The Reports export flow runs
    /// entirely under the umbrella "reports" area, but a couple of sections
    /// (exit-interview comments, the 90-day early-leaver list) have their own
    /// stricter sub-permission on the Settings page — this makes StoreAccessService
    /// honor that sub-permission here too, instead of just the umbrella "reports"
    /// toggle, so restricting the sub-area also restricts the exported report.</summary>
    private async Task<T> WithAreaAsync<T>(string area, Func<Task<T>> fetch)
    {
        var previous = _areaContext.Area;
        _areaContext.Area = area;
        try
        {
            return await fetch();
        }
        finally
        {
            _areaContext.Area = previous;
        }
    }

    /// <summary>Latest known Head Manager/OC/SOC/OM/OD per store, scoped to what
    /// this role/user can see — looked up once per report build and reused to
    /// enrich every store-scoped sheet, since none of the underlying per-report
    /// services (90-day, retention, exit interviews, early warning…) carry all
    /// five leadership fields on their own row types.</summary>
    private async Task<Dictionary<string, StoreReference>> BuildLeadershipMapAsync(string role, string? assignedName)
    {
        var all = await _stores.GetStoresAsync(null, null, role, assignedName);
        return all
            .Select(s => (Store: s, Key: StoreKey(s.StoreName)))
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Store.Year * 100 + x.Store.Month)
                    .ThenByDescending(x => x.Store.Id)
                    .Select(x => x.Store)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes the 5 leadership cells (Head Manager, OC, SOC, OM, OD) for
    /// a store starting at <paramref name="col"/> and returns the next free
    /// column index.</summary>
    private static int WriteLeadershipCells(IXLWorksheet ws, int row, int col, Dictionary<string, StoreReference> map, string? storeName)
    {
        map.TryGetValue(StoreKey(storeName), out var s);
        ws.Cell(row, col).Value = SafeText(s?.HeadManager ?? "—");
        ws.Cell(row, col + 1).Value = SafeText(s?.OperationConsultant ?? "—");
        ws.Cell(row, col + 2).Value = SafeText(s?.SeniorOperationConsultant ?? "—");
        ws.Cell(row, col + 3).Value = SafeText(s?.OperationManager ?? "—");
        ws.Cell(row, col + 4).Value = SafeText(s?.OperationDirector ?? "—");
        return col + 5;
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

    private static void SetPercentCell(IXLCell cell, double value)
    {
        cell.Value = value / 100.0;
        cell.Style.NumberFormat.Format = PercentFormat;
    }

    private static void SetNullablePercentCell(IXLCell cell, double? value)
    {
        if (value.HasValue) SetPercentCell(cell, value.Value);
        else cell.Value = "—";
    }

    private static void SetDateCell(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static void SetDateCell(IXLCell cell, DateTime? date)
    {
        if (date.HasValue)
        {
            cell.Value = date.Value;
            cell.Style.DateFormat.Format = DateFormat;
        }
        else cell.Value = "—";
    }

    /// <summary>Borders, zebra striping, auto-filter, and a frozen header
    /// row — applied once a sheet's data is fully written.</summary>
    private static void Finalize(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        if (lastRow < 1 || lastCol < 1) { ws.Columns().AdjustToContents(); return; }

        var range = ws.Range(1, 1, lastRow, lastCol);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor);
        range.Style.Border.InsideBorderColor = XLColor.FromHtml(GridColor);

        for (int r = 3; r <= lastRow; r += 2)
            ws.Range(r, 1, r, lastCol).Style.Fill.BackgroundColor = XLColor.FromHtml(BandFill);

        if (lastRow > 1) range.SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    /// <summary>ClosedXML 0.102+ requires explicit double cast (int has no
    /// direct implicit conversion to XLCellValue) AND an explicit NumberFormat
    /// to prevent Excel from rendering the cell as blank.</summary>
    private static void SetIntCell(IXLCell cell, int value)
    {
        cell.Value = (double)value;
        cell.Style.NumberFormat.Format = "#,##0";
    }

    private static void WriteLabelValueSheet(XLWorkbook wb, string sheetName, string labelHeader, string valueHeader, IEnumerable<ChartDataItem> items, bool asPercent = false)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { labelHeader, valueHeader });
        int r = 2;
        foreach (var item in items)
        {
            ws.Cell(r, 1).Value = SafeText(item.Label);
            if (asPercent) SetPercentCell(ws.Cell(r, 2), item.Value);
            else SetIntCell(ws.Cell(r, 2), item.Value);
            r++;
        }
        Finalize(ws);
    }

    // ── Store Comparison ────────────────────────────────────
    private async Task AddStoreComparisonSheetAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var rows = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var period = $"{month:D2}-{year}";
        var ws = AddSheet(wb, "Store Comparison");
        StyleHeader(ws, new[] { "Store", "Period" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Headcount", "New Hires", "Resignations", "Turnover Rate" }).ToArray());
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            ws.Cell(r + 2, 1).Value = SafeText(row.StoreName);
            ws.Cell(r + 2, 2).Value = period;
            int col = WriteLeadershipCells(ws, r + 2, 3, leadership, row.StoreName);
            SetIntCell(ws.Cell(r + 2, col), row.Headcount);
            SetIntCell(ws.Cell(r + 2, col + 1), row.NewHires);
            SetIntCell(ws.Cell(r + 2, col + 2), row.Resignations);
            SetPercentCell(ws.Cell(r + 2, col + 3), row.TurnoverRate);
        }
        Finalize(ws);
    }

    public async Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var wb = new XLWorkbook();
        await AddStoreComparisonSheetAsync(wb, month, year, role, assignedName, om, oc, soc, od);
        return wb;
    }

    // ── Turnover (mirrors the 90-Day Turnover report's shape: a company-wide
    // trend across every uploaded period, the latest period broken down by
    // store, and the full detailed record list) ─────────────────────
    private async Task AddTurnoverSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var periods = (await _dashboard.GetAvailablePeriodsAsync())
            .OrderBy(p => p.Year).ThenBy(p => p.Month).ToList();
        var leadership = await BuildLeadershipMapAsync(role, assignedName);

        var wsTrend = AddSheet(wb, "Turnover Trend");
        StyleHeader(wsTrend, new[] { "Period", "Headcount", "New Hires", "Resignations", "Turnover Rate" });
        for (int i = 0; i < periods.Count; i++)
        {
            var p = periods[i];
            var kpi = await _dashboard.GetKpisAsync(p.Month, p.Year, store, role, assignedName);
            wsTrend.Cell(i + 2, 1).Value = $"{p.Month:D2}-{p.Year}";
            SetIntCell(wsTrend.Cell(i + 2, 2), kpi.TotalHeadcount);
            SetIntCell(wsTrend.Cell(i + 2, 3), kpi.NewHires);
            SetIntCell(wsTrend.Cell(i + 2, 4), kpi.TotalResignations);
            SetPercentCell(wsTrend.Cell(i + 2, 5), kpi.TurnoverRate);
        }
        Finalize(wsTrend);

        if (periods.Count > 0)
        {
            var latest = periods[^1]; // ordered oldest→newest above, so the latest is last
            var byStore = await _dashboard.GetStoreComparisonAsync(latest.Month, latest.Year, role, assignedName);
            var wsByStore = AddSheet(wb, $"Turnover By Store ({latest.Month}-{latest.Year})");
            StyleHeader(wsByStore, new[] { "Store", "Period" }.Concat(LeadershipHeaders)
                .Concat(new[] { "Headcount", "New Hires", "Resignations", "Turnover Rate" }).ToArray());
            for (int i = 0; i < byStore.Count; i++)
            {
                var row = byStore[i];
                wsByStore.Cell(i + 2, 1).Value = SafeText(row.StoreName);
                wsByStore.Cell(i + 2, 2).Value = $"{latest.Month:D2}-{latest.Year}";
                int col = WriteLeadershipCells(wsByStore, i + 2, 3, leadership, row.StoreName);
                SetIntCell(wsByStore.Cell(i + 2, col), row.Headcount);
                SetIntCell(wsByStore.Cell(i + 2, col + 1), row.NewHires);
                SetIntCell(wsByStore.Cell(i + 2, col + 2), row.Resignations);
                SetPercentCell(wsByStore.Cell(i + 2, col + 3), row.TurnoverRate);
            }
            Finalize(wsByStore);
        }

        var resignations = await _dashboard.GetResignationDetailsAsync(store, role, assignedName);
        var wsResignations = AddSheet(wb, "Resignations (All)");
        StyleHeader(wsResignations, new[] { "Period", "Name", "Store" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Job Title", "Gender", "Hire Date", "Resignation Date", "Tenure (days)" }).ToArray());
        for (int i = 0; i < resignations.Count; i++)
        {
            var r = resignations[i];
            wsResignations.Cell(i + 2, 1).Value = $"{r.Month:D2}-{r.Year}";
            wsResignations.Cell(i + 2, 2).Value = SafeText(r.Name);
            wsResignations.Cell(i + 2, 3).Value = SafeText(r.Store);
            int col = WriteLeadershipCells(wsResignations, i + 2, 4, leadership, r.Store);
            wsResignations.Cell(i + 2, col).Value = SafeText(r.JobTitle);
            wsResignations.Cell(i + 2, col + 1).Value = SafeText(r.Gender);
            if (r.HireDate.HasValue) SetDateCell(wsResignations.Cell(i + 2, col + 2), r.HireDate.Value);
            else wsResignations.Cell(i + 2, col + 2).Value = "—";
            if (r.ResignationDate.HasValue) SetDateCell(wsResignations.Cell(i + 2, col + 3), r.ResignationDate.Value);
            else wsResignations.Cell(i + 2, col + 3).Value = "—";
            if (r.HireDate.HasValue && r.ResignationDate.HasValue)
                SetIntCell(wsResignations.Cell(i + 2, col + 4), r.ResignationDate.Value.DayNumber - r.HireDate.Value.DayNumber);
            else
                wsResignations.Cell(i + 2, col + 4).Value = "—";
        }
        Finalize(wsResignations);

        var byJobTitle = await _dashboard.GetTurnoverByJobTitleAsync(null, null, store, role, assignedName);
        var byTenure = await _dashboard.GetTurnoverByTenureAsync(null, null, store, role, assignedName);
        WriteLabelValueSheet(wb, "Turnover By Job Title", "Job Title", "Resignations", byJobTitle);
        WriteLabelValueSheet(wb, "Turnover By Tenure", "Tenure Bucket", "Resignations", byTenure);
    }

    public async Task<XLWorkbook> BuildTurnoverReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddTurnoverSheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── 90-Day Turnover ─────────────────────────────────────
    private async Task AddNinetyDaySheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var periods = await _ninetyDay.GetCohortPeriodsAsync();
        var trend = await _ninetyDay.GetTrendAsync(store, role, assignedName);

        var wsTrend = AddSheet(wb, "90D Cohort Trend");
        StyleHeader(wsTrend, new[] { "Cohort", "Total Hires", "Early Leavers", "Rate", "Provisional" });
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = SafeText(t.Label);
            SetIntCell(wsTrend.Cell(i + 2, 2), t.TotalHires);
            SetIntCell(wsTrend.Cell(i + 2, 3), t.EarlyLeavers);
            SetPercentCell(wsTrend.Cell(i + 2, 4), t.Rate);
            wsTrend.Cell(i + 2, 5).Value = t.IsProvisional ? "Yes" : "No";
        }
        Finalize(wsTrend);

        var leadership = await BuildLeadershipMapAsync(role, assignedName);

        if (periods.Count > 0)
        {
            var latest = periods[0]; // most recent first, per GetCohortPeriodsAsync contract
            var byStore = await _ninetyDay.GetByStoreAsync(latest.Month, latest.Year, role, assignedName);
            var wsByStore = AddSheet(wb, $"90D By Store ({latest.Month}-{latest.Year})");
            StyleHeader(wsByStore, new[] { "Store", "Period" }.Concat(LeadershipHeaders).Concat(new[] { "Early Leave Rate" }).ToArray());
            int bi = 2;
            foreach (var item in byStore)
            {
                wsByStore.Cell(bi, 1).Value = SafeText(item.Label);
                wsByStore.Cell(bi, 2).Value = $"{latest.Month:D2}-{latest.Year}";
                int c = WriteLeadershipCells(wsByStore, bi, 3, leadership, item.Label);
                SetPercentCell(wsByStore.Cell(bi, c), item.Value);
                bi++;
            }
            Finalize(wsByStore);
        }

        var wsLeavers = AddSheet(wb, "90D Early Leavers (All)");
        StyleHeader(wsLeavers, new[] { "Cohort", "Name", "Store" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Job Title", "Hire Date", "Resignation Date", "Tenure (days)" }).ToArray());
        int row = 2;
        var reasonTotals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var leavers = await WithAreaAsync(AccessAreas.NinetyDayLeavers,
                () => _ninetyDay.GetEarlyLeaversAsync(p.Month, p.Year, store, role, assignedName));
            var cohortLabel = $"{p.Month}-{p.Year}";
            foreach (var lv in leavers)
            {
                wsLeavers.Cell(row, 1).Value = cohortLabel;
                wsLeavers.Cell(row, 2).Value = SafeText(lv.Name);
                wsLeavers.Cell(row, 3).Value = SafeText(lv.Store);
                int c = WriteLeadershipCells(wsLeavers, row, 4, leadership, lv.Store);
                wsLeavers.Cell(row, c).Value = SafeText(lv.JobTitle);
                SetDateCell(wsLeavers.Cell(row, c + 1), lv.HireDate);
                SetDateCell(wsLeavers.Cell(row, c + 2), lv.ResignationDate);
                SetIntCell(wsLeavers.Cell(row, c + 3), lv.TenureDays);
                row++;
            }

            var reasons = await _ninetyDay.GetEarlyLeaverReasonsAsync(p.Month, p.Year, store, role, assignedName);
            foreach (var reason in reasons)
                reasonTotals[reason.Label] = reasonTotals.GetValueOrDefault(reason.Label) + reason.Value;
        }
        Finalize(wsLeavers);

        WriteLabelValueSheet(wb, "90D Reasons (Aggregated)", "Reason", "Count",
            reasonTotals.OrderByDescending(kv => kv.Value).Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }));
    }

    public async Task<XLWorkbook> BuildNinetyDayReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddNinetyDaySheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── Retention ───────────────────────────────────────────
    /// <summary>Retention has 5 sheets that each answer a different question
    /// about the same underlying data — without this legend the sheet names
    /// alone don't make that distinction obvious.</summary>
    private static void AddRetentionGuideSheet(XLWorkbook wb)
    {
        var ws = AddSheet(wb, "Guide - Read Me First");
        StyleHeader(ws, new[] { "Sheet", "What It Shows" });
        var rows = new (string Sheet, string Desc)[]
        {
            ("Retention Milestones", "One row per fixed milestone (90 Days, 6 Months, 1 Year, etc.) — what % of everyone hired long enough ago to have reached that milestone is still employed today. \"Through Cohort\" is the most recent hire-month old enough to be measured at that milestone."),
            ("Survival Curve", "Retention rate at every single day since hire (Day 0 through the longest tenure with enough data), across all hires — shows the shape of when people actually tend to leave (e.g. a cliff at 90 days)."),
            ("Retention Trend", "One row per hire-cohort month, showing that cohort's retention rate at each milestone (6 Months, 1 Year, etc.) — lets you compare whether newer cohorts are retaining better or worse than older ones. \"Provisional\" = Yes means that cohort hasn't been employed long enough yet for that milestone to be a final number — it can still change."),
            ("Store Leaderboard (1yr)", "Each store's 1-year retention rate, ranked highest to lowest."),
            ("Workforce Tenure", "The CURRENT active workforce grouped by how long they've been employed (tenure bucket) — a snapshot, not a rate."),
            ("Retention Insights", "Auto-generated plain-language callouts (e.g. notable stores or trends) based on the data in the other sheets."),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Sheet;
            ws.Cell(i + 2, 1).Style.Font.Bold = true;
            ws.Cell(i + 2, 2).Value = rows[i].Desc;
            ws.Cell(i + 2, 2).Style.Alignment.WrapText = true;
        }
        Finalize(ws);
        // Overrides Finalize's AdjustToContents (which measures wrapped text
        // badly — it sizes to the single longest unbroken word) with fixed
        // widths, then lets rows grow tall enough for the wrapped paragraphs.
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 90;
        ws.Rows().AdjustToContents();
    }

    private async Task AddRetentionSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var milestones = await _retention.GetMilestonesAsync(store, role, assignedName);
        var survival = await _retention.GetSurvivalCurveAsync(store, role, assignedName);
        var trend = await _retention.GetTrendAsync(store, role, assignedName);
        var leaderboard = await _retention.GetStoreLeaderboardAsync(role, assignedName);
        var tenureDist = await _retention.GetTenureDistributionAsync(store, role, assignedName);
        var insights = await _retention.GetInsightsAsync(store, role, assignedName);

        AddRetentionGuideSheet(wb);

        var wsMilestones = AddSheet(wb, "Retention Milestones");
        StyleHeader(wsMilestones, new[] { "Days", "Retention Rate", "Total Hires", "Retained", "Through Cohort" });
        for (int i = 0; i < milestones.Count; i++)
        {
            var m = milestones[i];
            SetIntCell(wsMilestones.Cell(i + 2, 1), m.Days);
            SetPercentCell(wsMilestones.Cell(i + 2, 2), m.RetentionRate);
            SetIntCell(wsMilestones.Cell(i + 2, 3), m.TotalHires);
            SetIntCell(wsMilestones.Cell(i + 2, 4), m.Retained);
            wsMilestones.Cell(i + 2, 5).Value = m.ThroughCohortLabel;
        }
        Finalize(wsMilestones);

        var wsSurvival = AddSheet(wb, "Survival Curve");
        StyleHeader(wsSurvival, new[] { "Day", "Retention Rate", "Sample Size" });
        for (int i = 0; i < survival.Count; i++)
        {
            var s = survival[i];
            SetIntCell(wsSurvival.Cell(i + 2, 1), s.Day);
            SetPercentCell(wsSurvival.Cell(i + 2, 2), s.RetentionRate);
            SetIntCell(wsSurvival.Cell(i + 2, 3), s.SampleSize);
        }
        Finalize(wsSurvival);

        var milestoneLabels = new[] { "6 Months", "1 Year", "2 Years", "3 Years", "4 Years", "5 Years" };
        var wsTrend = AddSheet(wb, "Retention Trend");
        StyleHeader(wsTrend, milestoneLabels.SelectMany(l => new[] { l, "Provisional" }).Prepend("Cohort").ToArray());
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = SafeText(t.Label);
            for (int m = 0; m < milestoneLabels.Length; m++)
            {
                var label = milestoneLabels[m];
                SetNullablePercentCell(wsTrend.Cell(i + 2, 2 + m * 2), t.Rates.TryGetValue(label, out var rate) ? rate : null);
                wsTrend.Cell(i + 2, 3 + m * 2).Value = t.Provisional.TryGetValue(label, out var prov) && prov ? "Yes" : "No";
            }
        }
        Finalize(wsTrend);

        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var wsLeaderboard = AddSheet(wb, "Store Leaderboard (1yr)");
        StyleHeader(wsLeaderboard, new[] { "Store", "Period" }.Concat(LeadershipHeaders).Concat(new[] { "Retention Rate" }).ToArray());
        for (int i = 0; i < leaderboard.Count; i++)
        {
            wsLeaderboard.Cell(i + 2, 1).Value = SafeText(leaderboard[i].Label);
            wsLeaderboard.Cell(i + 2, 2).Value = "Trailing 1 Year";
            int c = WriteLeadershipCells(wsLeaderboard, i + 2, 3, leadership, leaderboard[i].Label);
            SetPercentCell(wsLeaderboard.Cell(i + 2, c), leaderboard[i].Value);
        }
        Finalize(wsLeaderboard);

        WriteLabelValueSheet(wb, "Workforce Tenure", "Tenure Bucket", "Employees", tenureDist);

        var wsInsights = AddSheet(wb, "Retention Insights");
        StyleHeader(wsInsights, new[] { "Insight", "Description" });
        for (int i = 0; i < insights.Count; i++)
        {
            wsInsights.Cell(i + 2, 1).Value = SafeText(insights[i].Title);
            wsInsights.Cell(i + 2, 2).Value = SafeText(insights[i].Description);
        }
        Finalize(wsInsights);
    }

    public async Task<XLWorkbook> BuildRetentionReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddRetentionSheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── Exit Interviews (aggregate only — never names or IDs) ──
    private async Task AddExitInterviewSheetsAsync(XLWorkbook wb, string role, string? assignedName, ExitInterviewFilter? filterOverride = null)
    {
        var filter = filterOverride ?? new ExitInterviewFilter();

        var reasons = await _exitInterviews.GetReasonsForLeavingAsync(filter, role, assignedName);
        var wouldReturn = await _exitInterviews.GetWouldReturnAsync(filter, role, assignedName);
        var overallExperience = await _exitInterviews.GetOverallExperienceAsync(filter, role, assignedName);
        var workload = await _exitInterviews.GetWorkloadConditionAsync(filter, role, assignedName);
        var drivers = await _exitInterviews.GetEngagementDriversAsync(filter, role, assignedName);
        var comments = await WithAreaAsync(AccessAreas.ExitComments,
            () => _exitInterviews.GetCommentsAsync(filter, role, assignedName));

        WriteLabelValueSheet(wb, "Reasons for Leaving", "Reason", "Count", reasons);
        WriteLabelValueSheet(wb, "Would Return", "Answer", "Count", wouldReturn);
        WriteLabelValueSheet(wb, "Overall Experience", "Answer", "Count", overallExperience);
        WriteLabelValueSheet(wb, "Workload Condition", "Answer", "Count", workload);

        var wsDrivers = AddSheet(wb, "Engagement Drivers");
        StyleHeader(wsDrivers, new[] { "Driver", "Positive", "Total Responses" });
        for (int i = 0; i < drivers.Count; i++)
        {
            wsDrivers.Cell(i + 2, 1).Value = SafeText(drivers[i].Label);
            SetPercentCell(wsDrivers.Cell(i + 2, 2), drivers[i].PositivePercent);
            SetIntCell(wsDrivers.Cell(i + 2, 3), drivers[i].TotalResponses);
        }
        Finalize(wsDrivers);

        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var wsComments = AddSheet(wb, "Comments (Anonymous)");
        StyleHeader(wsComments, new[] { "Store", "Store Leader" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Question", "Comment", "Submitted At" }).ToArray());
        for (int i = 0; i < comments.Count; i++)
        {
            var c = comments[i];
            wsComments.Cell(i + 2, 1).Value = SafeText(c.Store);
            wsComments.Cell(i + 2, 2).Value = SafeText(c.StoreLeader);
            int col = WriteLeadershipCells(wsComments, i + 2, 3, leadership, c.Store);
            wsComments.Cell(i + 2, col).Value = SafeText(c.QuestionLabel);
            wsComments.Cell(i + 2, col + 1).Value = SafeText(c.Text);
            SetDateCell(wsComments.Cell(i + 2, col + 2), c.SubmittedAt);
        }
        Finalize(wsComments);
    }

    public async Task<XLWorkbook> BuildExitInterviewReportAsync(string role, string? assignedName, string? store = null, string? om = null, string? oc = null)
    {
        var wb = new XLWorkbook();
        await AddExitInterviewSheetsAsync(wb, role, assignedName, new ExitInterviewFilter { Store = store, OperationConsultant = oc, OperationManager = om });
        return wb;
    }

    /// <summary>Human-readable description of a Year+Months filter selection,
    /// for reports whose rows are rolled up across a period range rather than
    /// tied to one specific month.</summary>
    private static string DescribePeriodScope(string? months, int? year)
    {
        if (!year.HasValue) return "All Periods";
        var monthList = MultiValueFilter.Split(months);
        if (monthList == null || monthList.Count == 0) return $"{year} (All Months)";
        var names = monthList.Select(m => int.TryParse(m, out var mi) && mi is >= 1 and <= 12
            ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(mi) : m);
        return $"{year} ({string.Join(", ", names)})";
    }

    private static string DescribeFilter(string? value)
    {
        var values = MultiValueFilter.Split(value);
        return values == null || values.Count == 0 ? "All" : string.Join(", ", values);
    }

    // ── Scorecard ───────────────────────────────────────────
    private async Task AddScorecardSheetAsync(XLWorkbook wb, string dimension, string sheetName, string nameHeader, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        var rows = await _scorecard.GetScorecardAsync(dimension, role, assignedName, om, oc, soc, od, months, year);
        var period = DescribePeriodScope(months, year);
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { nameHeader, "Period", "Stores", "Headcount", "Turnover Rate", "90-Day Early Leave", "180-Day Retention", "Exit Sentiment", "Exit Responses" });
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = SafeText(r.Name);
            ws.Cell(i + 2, 2).Value = period;
            SetIntCell(ws.Cell(i + 2, 3), r.StoreCount);
            SetIntCell(ws.Cell(i + 2, 4), r.Headcount);
            SetPercentCell(ws.Cell(i + 2, 5), r.TurnoverRate);
            SetPercentCell(ws.Cell(i + 2, 6), r.EarlyLeaver90Rate);
            SetPercentCell(ws.Cell(i + 2, 7), r.Retention180Rate);
            if (r.ExitResponseCount > 0) SetPercentCell(ws.Cell(i + 2, 8), r.ExitSentimentPercent);
            else ws.Cell(i + 2, 8).Value = "—";
            SetIntCell(ws.Cell(i + 2, 9), r.ExitResponseCount);
        }
        Finalize(ws);
    }

    private async Task AddScorecardSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        await AddScorecardSheetAsync(wb, "leader", "Scorecard Store Leaders", "Store Leader", role, assignedName, om, oc, soc, od, months, year);
        await AddScorecardSheetAsync(wb, "oc", "Scorecard Op. Consultants", "Operation Consultant", role, assignedName, om, oc, soc, od, months, year);
        await AddScorecardSheetAsync(wb, "om", "Scorecard Op. Managers", "Operation Manager", role, assignedName, om, oc, soc, od, months, year);
    }

    public async Task<XLWorkbook> BuildScorecardReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        var wb = new XLWorkbook();
        await AddScorecardSheetsAsync(wb, role, assignedName, om, oc, soc, od, months, year);
        return wb;
    }

    // ── Early Warning ───────────────────────────────────────
    private async Task AddEarlyWarningSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null,
        string? om = null, string? oc = null, string? soc = null, string? od = null,
        string? months = null, int? year = null)
    {
        var summary = await _earlyWarning.GetSummaryAsync(store, role, assignedName, months, year, om, oc, soc, od);
        var watchlist = await _earlyWarning.GetWatchlistAsync(store, role, assignedName, months, year, om, oc, soc, od);
        var periodScope = year.HasValue ? DescribePeriodScope(months, year) : "Latest Uploaded Period";

        var wsSummary = AddSheet(wb, "Early Warning Summary");
        StyleHeader(wsSummary, new[] { "Metric", "Value" });
        wsSummary.Cell(2, 1).Value = "Selected Snapshot"; wsSummary.Cell(2, 2).Value = periodScope;
        wsSummary.Cell(3, 1).Value = "Store Filter"; wsSummary.Cell(3, 2).Value = DescribeFilter(store);
        wsSummary.Cell(4, 1).Value = "Operation Manager Filter"; wsSummary.Cell(4, 2).Value = DescribeFilter(om);
        wsSummary.Cell(5, 1).Value = "Operation Consultant Filter"; wsSummary.Cell(5, 2).Value = DescribeFilter(oc);
        wsSummary.Cell(6, 1).Value = "Senior Operation Consultant Filter"; wsSummary.Cell(6, 2).Value = DescribeFilter(soc);
        wsSummary.Cell(7, 1).Value = "Operation Director Filter"; wsSummary.Cell(7, 2).Value = DescribeFilter(od);
        wsSummary.Cell(8, 1).Value = "Total On Watchlist"; SetIntCell(wsSummary.Cell(8, 2), summary.TotalWatchlist);
        wsSummary.Cell(9, 1).Value = "High Risk (4–5 stars)"; SetIntCell(wsSummary.Cell(9, 2), summary.HighRiskCount);
        wsSummary.Cell(10, 1).Value = "In First 90 Days"; SetIntCell(wsSummary.Cell(10, 2), summary.NewHireWindowCount);
        wsSummary.Cell(11, 1).Value = "Company Baseline Early-Leave Rate"; SetPercentCell(wsSummary.Cell(11, 2), summary.CompanyBaselineRate);
        Finalize(wsSummary);

        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var asOf = DateOnly.FromDateTime(DateTime.Now).ToString(DateFormat);
        var wsWatchlist = AddSheet(wb, "Early Warning Watchlist");
        StyleHeader(wsWatchlist, new[] { "Name", "Store", "Period" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Job Title", "Hire Date", "Tenure (days)", "Risk Stars (1-5)", "Raw Score", "Reasons" }).ToArray());
        for (int i = 0; i < watchlist.Count; i++)
        {
            var w = watchlist[i];
            wsWatchlist.Cell(i + 2, 1).Value = SafeText(w.Name);
            wsWatchlist.Cell(i + 2, 2).Value = SafeText(w.Store);
            wsWatchlist.Cell(i + 2, 3).Value = year.HasValue ? $"Selected: {periodScope}" : $"As Of {asOf}";
            int col = WriteLeadershipCells(wsWatchlist, i + 2, 4, leadership, w.Store);
            wsWatchlist.Cell(i + 2, col).Value = SafeText(w.JobTitle);
            SetDateCell(wsWatchlist.Cell(i + 2, col + 1), w.HireDate);
            SetIntCell(wsWatchlist.Cell(i + 2, col + 2), w.TenureDays);
            var stars = Math.Clamp(w.Stars, 0, 5);
            wsWatchlist.Cell(i + 2, col + 3).Value = new string('★', stars) + new string('☆', 5 - stars);
            SetIntCell(wsWatchlist.Cell(i + 2, col + 4), w.RiskScore);
            wsWatchlist.Cell(i + 2, col + 5).Value = string.Join(" | ", w.Reasons.Select(r => r.Type));
        }
        Finalize(wsWatchlist);
    }

    public async Task<XLWorkbook> BuildEarlyWarningReportAsync(string role, string? assignedName, string? store = null,
        string? om = null, string? oc = null, string? soc = null, string? od = null,
        string? months = null, int? year = null)
    {
        var wb = new XLWorkbook();
        await AddEarlyWarningSheetsAsync(wb, role, assignedName, store, om, oc, soc, od, months, year);
        return wb;
    }

    // ── Trend Matrix ─────────────────────────────────────────
    private static void WriteTrendMatrixSheet(XLWorkbook wb, string sheetName, TrendMatrixResult result, Dictionary<string, StoreReference> leadership)
    {
        var ws = AddSheet(wb, sheetName);

        var headerList = new List<string> { "Store" };
        headerList.AddRange(LeadershipHeaders);
        headerList.AddRange(result.Periods);
        headerList.Add("Total");
        StyleHeader(ws, headerList.ToArray());

        for (int i = 0; i < result.Rows.Count; i++)
        {
            var row = result.Rows[i];
            ws.Cell(i + 2, 1).Value = SafeText(row.StoreName);
            int col = WriteLeadershipCells(ws, i + 2, 2, leadership, row.StoreName);
            // Sum of this row's own displayed period rates (not an average/pooled
            // rate) — matches the "Total" column on the Turnover/90-Day Turnover
            // dashboard pages (mxTotalRate), which replaced their old AVG column.
            double total = 0;
            for (int p = 0; p < result.Periods.Count; p++)
            {
                if (row.PeriodRates.TryGetValue(result.Periods[p], out var rate) && rate.HasValue)
                {
                    SetPercentCell(ws.Cell(i + 2, col + p), rate.Value);
                    total += rate.Value;
                }
                else
                {
                    ws.Cell(i + 2, col + p).Value = "—";
                }
            }
            SetPercentCell(ws.Cell(i + 2, col + result.Periods.Count), total);
        }
        Finalize(ws);
    }

    private async Task AddTrendMatrixSheetAsync(XLWorkbook wb, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null)
    {
        var result = await _dashboard.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, sinceYear, months);
        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        WriteTrendMatrixSheet(wb, "Turnover Trend Matrix", result, leadership);
    }

    public async Task<XLWorkbook> BuildTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null)
    {
        var wb = new XLWorkbook();
        await AddTrendMatrixSheetAsync(wb, role, assignedName, om, oc, soc, od, sinceYear, months);
        return wb;
    }

    // ── 90-Day Trend Matrix ────────────────────────────────────
    public async Task<XLWorkbook> BuildNinetyDayTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null)
    {
        var wb = new XLWorkbook();
        var result = await _ninetyDay.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, months, sinceYear);
        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        WriteTrendMatrixSheet(wb, "90-Day Trend Matrix", result, leadership);
        return wb;
    }

    // ── Action Center ────────────────────────────────────────
    // Exports only the store-level "AC Stores" sheet (per the reports rework —
    // the company-wide Summary/Top Reasons/By Region/Monthly Trend sheets were
    // dropped since they're already visible on the Action Center dashboard
    // page itself and just duplicated it in the export).
    private async Task AddActionCenterSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var stores = await _actionPlans.GetActionCenterStoresAsync(role, assignedName);
        var leadership = await BuildLeadershipMapAsync(role, assignedName);

        var oms = MultiValueFilter.Split(om);
        var ocs = MultiValueFilter.Split(oc);
        var socs = MultiValueFilter.Split(soc);
        var ods = MultiValueFilter.Split(od);
        if (oms != null || ocs != null || socs != null || ods != null)
        {
            stores = stores.Where(s =>
            {
                leadership.TryGetValue(StoreKey(s.StoreName), out var l);
                if (oms != null && !oms.Contains(l?.OperationManager ?? "")) return false;
                if (ocs != null && !ocs.Contains(l?.OperationConsultant ?? "")) return false;
                if (socs != null && !socs.Contains(l?.SeniorOperationConsultant ?? "")) return false;
                if (ods != null && !ods.Contains(l?.OperationDirector ?? "")) return false;
                return true;
            }).ToList();
        }

        var asOf = DateOnly.FromDateTime(DateTime.Now).ToString(DateFormat);
        var wsStores = AddSheet(wb, "AC Stores");
        StyleHeader(wsStores, new[] { "Store", "Period" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Plan Status", "Severity", "Signals", "Age (days)", "Chronic", "Stalled", "Trend", "Responsible", "Assigned To", "Target Date", "Tasks Done" }).ToArray());
        for (int i = 0; i < stores.Count; i++)
        {
            var s = stores[i];
            wsStores.Cell(i + 2, 1).Value = SafeText(s.StoreName);
            wsStores.Cell(i + 2, 2).Value = $"As Of {asOf}";
            int col = WriteLeadershipCells(wsStores, i + 2, 3, leadership, s.StoreName);
            wsStores.Cell(i + 2, col).Value = SafeText(s.PlanStatus);
            wsStores.Cell(i + 2, col + 1).Value = SafeText(s.Severity);
            SetIntCell(wsStores.Cell(i + 2, col + 2), s.SignalCount);
            SetIntCell(wsStores.Cell(i + 2, col + 3), s.AgeDays);
            wsStores.Cell(i + 2, col + 4).Value = s.IsChronic ? "Yes" : "No";
            wsStores.Cell(i + 2, col + 5).Value = s.IsStalled ? "Yes" : "No";
            wsStores.Cell(i + 2, col + 6).Value = SafeText(s.Trend);
            wsStores.Cell(i + 2, col + 7).Value = SafeText(s.ResponsibleName ?? "—");
            wsStores.Cell(i + 2, col + 8).Value = SafeText(s.AssignedToName ?? "—");
            SetDateCell(wsStores.Cell(i + 2, col + 9), s.TargetResolutionDate?.ToDateTime(TimeOnly.MinValue));
            wsStores.Cell(i + 2, col + 10).Value = s.TasksTotal > 0 ? $"{s.TasksCompleted}/{s.TasksTotal}" : "—";
        }
        Finalize(wsStores);
    }

    public async Task<XLWorkbook> BuildActionCenterReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var wb = new XLWorkbook();
        await AddActionCenterSheetsAsync(wb, role, assignedName, om, oc, soc, od);
        return wb;
    }

    // ── Stores Overview ──────────────────────────────────────
    private async Task AddStoresOverviewSheetAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var comparison = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var acStores = await _actionPlans.GetActionCenterStoresAsync(role, assignedName);
        var watchlist = await _earlyWarning.GetWatchlistAsync(null, role, assignedName);

        var acByStore = acStores.ToDictionary(s => s.StoreName, s => s);
        var highRiskByStore = watchlist.Where(w => w.Stars >= 4)
            .GroupBy(w => w.Store).ToDictionary(g => g.Key, g => g.Count());

        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var period = $"{month:D2}-{year}";
        var ws = AddSheet(wb, "Stores Overview");
        StyleHeader(ws, new[] { "Store", "Period" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Headcount", "New Hires", "Resignations", "Turnover Rate", "Action Plan Status", "Severity", "High-Risk Employees" }).ToArray());
        for (int i = 0; i < comparison.Count; i++)
        {
            var row = comparison[i];
            ws.Cell(i + 2, 1).Value = SafeText(row.StoreName);
            ws.Cell(i + 2, 2).Value = period;
            int col = WriteLeadershipCells(ws, i + 2, 3, leadership, row.StoreName);
            SetIntCell(ws.Cell(i + 2, col), row.Headcount);
            SetIntCell(ws.Cell(i + 2, col + 1), row.NewHires);
            SetIntCell(ws.Cell(i + 2, col + 2), row.Resignations);
            SetPercentCell(ws.Cell(i + 2, col + 3), row.TurnoverRate);
            var ac = acByStore.GetValueOrDefault(row.StoreName);
            ws.Cell(i + 2, col + 4).Value = ac?.PlanStatus ?? "None";
            ws.Cell(i + 2, col + 5).Value = ac?.Severity ?? "None";
            SetIntCell(ws.Cell(i + 2, col + 6), highRiskByStore.GetValueOrDefault(row.StoreName));
        }
        Finalize(ws);
    }

    public async Task<XLWorkbook> BuildStoresOverviewReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var wb = new XLWorkbook();
        await AddStoresOverviewSheetAsync(wb, month, year, role, assignedName, om, oc, soc, od);
        return wb;
    }

    // ── Workforce ────────────────────────────────────────────
    private async Task AddWorkforceSheetsAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        var details = await _dashboard.GetEmployeeDetailsAsync(month, year, store, role, assignedName, om, oc, soc, od);
        var kpi = await _dashboard.GetKpisAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byJobTitle = await _dashboard.GetHeadcountByJobTitleAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byPayrollGroup = await _dashboard.GetHeadcountByPayrollGroupAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byTenure = await _dashboard.GetHeadcountByTenureAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byGender = await _dashboard.GetGenderBreakdownAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var trend = await _dashboard.GetHeadcountTrendAsync(store, role, assignedName, om, oc, soc, od, sinceYear);

        // Detailed employee-level roster — first sheet, so opening the file lands
        // on the raw data before the aggregated breakdowns further in.
        var leadership = await BuildLeadershipMapAsync(role, assignedName);
        var period = $"{month:D2}-{year}";
        var wsDetail = AddSheet(wb, "Workforce Detail");
        StyleHeader(wsDetail, new[] { "Employee ID", "Name", "Store", "Period" }.Concat(LeadershipHeaders)
            .Concat(new[] { "Job Title", "Grade", "Payroll Group", "Gender", "Hire Date" }).ToArray());
        for (int i = 0; i < details.Count; i++)
        {
            var e = details[i];
            wsDetail.Cell(i + 2, 1).Value = SafeText(e.EmployeeId);
            wsDetail.Cell(i + 2, 2).Value = SafeText(e.Name);
            wsDetail.Cell(i + 2, 3).Value = SafeText(e.Store);
            wsDetail.Cell(i + 2, 4).Value = period;
            int col = WriteLeadershipCells(wsDetail, i + 2, 5, leadership, e.Store);
            wsDetail.Cell(i + 2, col).Value = SafeText(e.JobTitle);
            wsDetail.Cell(i + 2, col + 1).Value = SafeText(e.Grade);
            wsDetail.Cell(i + 2, col + 2).Value = SafeText(e.PayrollGroup);
            wsDetail.Cell(i + 2, col + 3).Value = SafeText(e.Gender);
            if (e.HireDate.HasValue) SetDateCell(wsDetail.Cell(i + 2, col + 4), e.HireDate.Value);
            else wsDetail.Cell(i + 2, col + 4).Value = "—";
        }
        Finalize(wsDetail);

        var wsKpi = AddSheet(wb, "Workforce KPIs");
        StyleHeader(wsKpi, new[] { "Metric", "Value" });
        wsKpi.Cell(2, 1).Value = "Total Headcount"; SetIntCell(wsKpi.Cell(2, 2), kpi.TotalHeadcount);
        wsKpi.Cell(3, 1).Value = "New Hires"; SetIntCell(wsKpi.Cell(3, 2), kpi.NewHires);
        wsKpi.Cell(4, 1).Value = "Total Resignations"; SetIntCell(wsKpi.Cell(4, 2), kpi.TotalResignations);
        wsKpi.Cell(5, 1).Value = "Turnover Rate"; SetPercentCell(wsKpi.Cell(5, 2), kpi.TurnoverRate);
        Finalize(wsKpi);

        WriteLabelValueSheet(wb, "Headcount By Job Title", "Job Title", "Headcount", byJobTitle);
        WriteLabelValueSheet(wb, "Headcount By Payroll Group", "Payroll Group", "Headcount", byPayrollGroup);
        WriteLabelValueSheet(wb, "Headcount By Tenure", "Tenure Bucket", "Headcount", byTenure);
        WriteLabelValueSheet(wb, "Headcount By Gender", "Gender", "Headcount", byGender);

        // Gender count per store — one row per store, one column per gender
        // value actually present in this period's roster (Male/Female first,
        // any other value after, blank Gender grouped under "Unspecified"),
        // built from the same employee roster as the Workforce Detail sheet
        // above so the two sheets never disagree.
        static string GenderKey(string g) => string.IsNullOrWhiteSpace(g) ? "Unspecified" : g;
        var genderValues = details.Select(e => GenderKey(e.Gender)).Distinct()
            .OrderBy(g => g == "Male" ? 0 : g == "Female" ? 1 : g == "Unspecified" ? 2 : 3)
            .ThenBy(g => g)
            .ToList();
        var storeGroups = details.GroupBy(e => e.Store).OrderBy(g => g.Key).ToList();
        var wsGenderByStore = AddSheet(wb, "Gender Count By Store");
        StyleHeader(wsGenderByStore, new[] { "Store" }.Concat(genderValues).Concat(new[] { "Total" }).ToArray());
        for (int i = 0; i < storeGroups.Count; i++)
        {
            var group = storeGroups[i];
            wsGenderByStore.Cell(i + 2, 1).Value = SafeText(group.Key);
            int col = 2;
            foreach (var gender in genderValues)
            {
                SetIntCell(wsGenderByStore.Cell(i + 2, col), group.Count(e => GenderKey(e.Gender) == gender));
                col++;
            }
            SetIntCell(wsGenderByStore.Cell(i + 2, col), group.Count());
        }
        Finalize(wsGenderByStore);

        var wsTrend = AddSheet(wb, "Headcount Trend");
        StyleHeader(wsTrend, new[] { "Period", "Headcount" });
        for (int i = 0; i < trend.Count; i++)
        {
            wsTrend.Cell(i + 2, 1).Value = SafeText(trend[i].Label);
            SetIntCell(wsTrend.Cell(i + 2, 2), trend[i].Value);
        }
        Finalize(wsTrend);
    }

    public async Task<XLWorkbook> BuildWorkforceReportAsync(int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        var wb = new XLWorkbook();
        await AddWorkforceSheetsAsync(wb, month, year, role, assignedName, store, om, oc, soc, od, sinceYear);
        return wb;
    }

    // ── Workforce Planning (projected vs actual) ─────────────
    private static List<int>? ParseMonthList(string? months) => string.IsNullOrWhiteSpace(months)
        ? null
        : months.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => int.TryParse(m, out var n) ? n : 0).Where(n => n is >= 1 and <= 12).Distinct().ToList();

    /// <summary>The detailed-data companion of the Workforce Planning report: the flat Data sheet (one row per
    /// store/job/month) and the five pivot tables built on it. Kept apart because it is by far the heaviest part.</summary>
    public async Task<XLWorkbook> BuildWorkforcePlanningDataReportAsync(int year, string? months, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var rows = await _planning.GetDetailAsync(year, ParseMonthList(months), store, jobs, role, assignedName, om, oc, soc, od);
        var wb = new XLWorkbook();
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        if (rows.Count == 0)
        {
            var note = AddSheet(wb, "Summary");
            note.Cell(1, 1).Value = "No projection data matches the selected filters.";
            note.Column(1).Width = 50;
            return wb;
        }

        // ── Data: one flat row per store/job/month — the pivot tables' source ──
        var data = AddSheet(wb, "Data");
        string[] dh = { "Period", "Year", "Month", "Store", "Job Title", "Projected", "Actual", "Gap", "Fill %", "Expected Resignations", "Hiring Need", "Operation Consultant", "Operation Manager", "Senior Operation Consultant", "Operation Director", "Shortage", "Payroll Group" };
        StyleHeader(data, dh);
        int dr = 2;
        foreach (var r in rows)
        {
            data.Cell(dr, 1).Value = $"{r.Year}-{r.Month:D2}";
            data.Cell(dr, 2).Value = (double)r.Year;
            data.Cell(dr, 3).Value = monthName[r.Month - 1];
            data.Cell(dr, 4).Value = SafeText(r.Store);
            data.Cell(dr, 5).Value = SafeText(r.Job);
            data.Cell(dr, 6).Value = (double)r.Projected;
            if (r.Actual.HasValue)
            {
                data.Cell(dr, 7).Value = (double)r.Actual.Value;
                data.Cell(dr, 8).Value = (double)(r.Actual.Value - r.Projected);
                data.Cell(dr, 16).Value = r.Shortage ?? 0.0;
                if (r.Projected > 0) { data.Cell(dr, 9).Value = r.Actual.Value / (double)r.Projected; data.Cell(dr, 9).Style.NumberFormat.Format = "0.0%"; }
                if (r.ExpectedAttrition.HasValue) data.Cell(dr, 10).Value = r.ExpectedAttrition.Value;
                if (r.HiringNeed.HasValue) data.Cell(dr, 11).Value = r.HiringNeed.Value;
            }
            data.Cell(dr, 12).Value = SafeText(r.OperationConsultant);
            data.Cell(dr, 13).Value = SafeText(r.OperationManager);
            data.Cell(dr, 14).Value = SafeText(r.SeniorOperationConsultant);
            data.Cell(dr, 15).Value = SafeText(r.OperationDirector);
            data.Cell(dr, 17).Value = string.IsNullOrWhiteSpace(r.PayrollGroup) ? "Unassigned" : SafeText(r.PayrollGroup);
            dr++;
        }
        var lastRow = dr - 1;
        data.SheetView.FreezeRows(1);
        data.Range(1, 1, lastRow, dh.Length).SetAutoFilter();
        data.Columns().AdjustToContents();

        // ── Pivot tables (Excel builds them from the Data sheet when the file opens) ──
        var source = data.Range(1, 1, lastRow, dh.Length);
        void AddPivot(string sheetName, string pivotName, string rowField, string? colField, params string[] valueFields)
        {
            var ws = AddSheet(wb, sheetName);
            var pt = ws.PivotTables.Add(pivotName, ws.Cell(3, 1), source);
            pt.RowLabels.Add(rowField);
            if (colField != null) pt.ColumnLabels.Add(colField);
            foreach (var v in valueFields)
                pt.Values.Add(v, $"Sum of {v}").SetSummaryFormula(XLPivotSummary.Sum);
            pt.ShowGrandTotalsRows = true;
            pt.ShowGrandTotalsColumns = true;
            ws.Cell(1, 1).Value = $"{sheetName} — refreshes when opened in Excel";
            ws.Cell(1, 1).Style.Font.Bold = true;
        }
        AddPivot("Pivot Store x Period", "PivotStorePeriod", "Store", "Period", "Projected");
        AddPivot("Pivot Job x Period", "PivotJobPeriod", "Job Title", "Period", "Projected");
        AddPivot("Pivot Store Gap", "PivotStoreGap", "Store", null, "Projected", "Actual", "Gap", "Shortage", "Hiring Need");
        AddPivot("Pivot Payroll Group", "PivotPayrollGroup", "Payroll Group", null, "Projected", "Actual", "Gap", "Shortage", "Hiring Need");
        AddPivot("Pivot Consultant Gap", "PivotConsultantGap", "Operation Consultant", null, "Projected", "Actual", "Gap", "Shortage", "Hiring Need");
        return wb;
    }

    public async Task<XLWorkbook> BuildWorkforcePlanningReportAsync(int year, string? months, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var monthList = ParseMonthList(months);
        var rows = await _planning.GetDetailAsync(year, monthList, store, jobs, role, assignedName, om, oc, soc, od);

        var wb = new XLWorkbook();
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;

        // ── Summary ──
        var sum = AddSheet(wb, "Summary");
        sum.Cell(1, 1).Value = $"Workforce Planning — Projected vs Actual Headcount ({year})";
        sum.Cell(1, 1).Style.Font.Bold = true; sum.Cell(1, 1).Style.Font.FontSize = 14;
        sum.Cell(2, 1).Value = "Store filter"; sum.Cell(2, 2).Value = string.IsNullOrWhiteSpace(store) ? "All accessible stores" : SafeText(store);
        sum.Cell(3, 1).Value = "Job filter"; sum.Cell(3, 2).Value = string.IsNullOrWhiteSpace(jobs) ? "All job titles" : SafeText(jobs);
        sum.Cell(4, 1).Value = "Months"; sum.Cell(4, 2).Value = monthList is { Count: > 0 } ? string.Join(", ", monthList.OrderBy(m => m).Select(m => monthName[m - 1])) : "All months in the projection";
        var responsible = string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(oc) ? null : "Consultant: " + oc,
            string.IsNullOrWhiteSpace(om) ? null : "Manager: " + om,
            string.IsNullOrWhiteSpace(soc) ? null : "Senior consultant: " + soc,
            string.IsNullOrWhiteSpace(od) ? null : "Director: " + od,
        }.Where(x => x != null));
        sum.Cell(5, 1).Value = "Responsible filter"; sum.Cell(5, 2).Value = responsible.Length == 0 ? "All" : SafeText(responsible);
        sum.Cell(6, 1).Value = "Generated"; sum.Cell(6, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        sum.Range(2, 1, 6, 1).Style.Font.Bold = true;
        sum.Cell(7, 1).Value = "Gap = Actual − Projected (positive = surplus, negative = shortage). Fill rate = Actual ÷ Projected. Shortage = people missing in the jobs that are short (a surplus in one job never offsets another). Hiring need = shortage + expected resignations (average monthly resignations of the last 6 roster months) — an estimate. Only stores with a projection for a month are compared in that month; months without an uploaded roster show the projection only.";
        sum.Cell(7, 1).Style.Font.Italic = true;
        // The title and the long note span the table instead of stretching column A.
        sum.Range(1, 1, 1, 8).Merge();
        sum.Range(7, 1, 7, 8).Merge().Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        sum.Row(7).Height = 78;

        if (rows.Count == 0)
        {
            sum.Cell(9, 1).Value = "No projection data matches the selected filters.";
            sum.Column(1).Width = 24;
            return wb;
        }

        // Per-month totals (live formulas for Gap and Fill so the sheet stays editable).
        const int hdr = 9;
        string[] mh = { "Month", "Projected", "Actual", "Gap", "Fill rate", "Shortage", "Expected resignations / month", "Hiring need (est.)" };
        for (int i = 0; i < mh.Length; i++)
        {
            var c = sum.Cell(hdr, i + 1);
            c.Value = mh[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
            c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        var byMonth = rows.GroupBy(r => r.Month).OrderBy(g => g.Key).ToList();
        int r0 = hdr + 1, rr = r0;
        foreach (var g in byMonth)
        {
            sum.Cell(rr, 1).Value = monthName[g.Key - 1];
            SetIntCell(sum.Cell(rr, 2), g.Sum(x => x.Projected));
            if (g.All(x => x.Actual.HasValue)) SetIntCell(sum.Cell(rr, 3), g.Sum(x => x.Actual!.Value));
            sum.Cell(rr, 4).FormulaA1 = $"=IF(C{rr}=\"\",\"\",C{rr}-B{rr})";
            sum.Cell(rr, 5).FormulaA1 = $"=IF(OR(C{rr}=\"\",B{rr}=0),\"\",C{rr}/B{rr})";
            sum.Cell(rr, 5).Style.NumberFormat.Format = "0.0%";
            if (g.All(x => x.Actual.HasValue)) SetIntCell(sum.Cell(rr, 6), (int)Math.Round(g.Sum(x => x.Shortage ?? 0), MidpointRounding.AwayFromZero));
            if (g.All(x => x.ExpectedAttrition.HasValue)) sum.Cell(rr, 7).Value = Math.Round(g.Sum(x => x.ExpectedAttrition!.Value), 1);
            if (g.All(x => x.HiringNeed.HasValue)) SetIntCell(sum.Cell(rr, 8), (int)Math.Round(g.Sum(x => x.HiringNeed!.Value), MidpointRounding.AwayFromZero));
            rr++;
        }
        sum.Range(r0, 1, rr - 1, 8).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sum.Range(r0, 1, rr - 1, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sum.Columns().AdjustToContents(hdr, rr - 1); // size columns from the table only, not the title and the note
        sum.Column(1).Width = Math.Max(sum.Column(1).Width, 20);
        for (int c = 2; c <= 8; c++) sum.Column(c).Width = Math.Max(sum.Column(c).Width, 14);

        // Snapshot month for the By Store / By Job sheets: the latest selected month
        // with an uploaded roster, else the latest selected month.
        var snapMonth = rows.Where(r => r.Actual.HasValue).Select(r => r.Month).DefaultIfEmpty(rows.Max(r => r.Month)).Max();
        var snap = rows.Where(r => r.Month == snapMonth).ToList();
        bool snapHasActual = snap.Any(r => r.Actual.HasValue);

        void WriteBreakdown(string sheet, string nameHeader, IEnumerable<(string Name, int P, int A, double Attr, double Need, double Short)> items)
        {
            var ws = AddSheet(wb, sheet);
            StyleHeader(ws, new[] { nameHeader, "Projected", "Actual", "Gap", "Fill rate", "Shortage", "Expected resignations / month", "Hiring need (est.)", "Status" });
            int r = 2;
            foreach (var it in items.OrderBy(i => snapHasActual ? i.A - i.P : -i.P).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                ws.Cell(r, 1).Value = SafeText(it.Name);
                SetIntCell(ws.Cell(r, 2), it.P);
                if (snapHasActual)
                {
                    SetIntCell(ws.Cell(r, 3), it.A);
                    ws.Cell(r, 4).FormulaA1 = $"=C{r}-B{r}";
                    ws.Cell(r, 5).FormulaA1 = $"=IF(B{r}=0,\"\",C{r}/B{r})";
                    ws.Cell(r, 5).Style.NumberFormat.Format = "0.0%";
                    SetIntCell(ws.Cell(r, 6), (int)Math.Round(it.Short, MidpointRounding.AwayFromZero));
                    ws.Cell(r, 7).Value = Math.Round(it.Attr, 1);
                    SetIntCell(ws.Cell(r, 8), (int)Math.Round(it.Need, MidpointRounding.AwayFromZero));
                    var fill = it.P > 0 ? it.A * 100.0 / it.P : 100.0;
                    ws.Cell(r, 9).Value = it.P > 0 && it.A > it.P ? "Overstaffed" : fill >= 95 ? "On track" : fill >= 85 ? "Watch" : "Understaffed";
                }
                else ws.Cell(r, 9).Value = "No actual data";
                r++;
            }
            Finalize(ws);
        }
        WriteBreakdown($"By Store ({monthName[snapMonth - 1]})", "Store", snap.GroupBy(r => r.Store).Select(g => (g.Key, g.Sum(x => x.Projected), g.Sum(x => x.Actual ?? 0), g.Sum(x => x.ExpectedAttrition ?? 0), g.Sum(x => x.HiringNeed ?? 0), g.Sum(x => x.Shortage ?? 0.0))));
        WriteBreakdown($"By Job ({monthName[snapMonth - 1]})", "Job title", snap.GroupBy(r => r.Job).Select(g => (g.Key, g.Sum(x => x.Projected), g.Sum(x => x.Actual ?? 0), g.Sum(x => x.ExpectedAttrition ?? 0), g.Sum(x => x.HiringNeed ?? 0), g.Sum(x => x.Shortage ?? 0.0))));


        // ── By Consultant & Manager: the page's four roll-up tables, stacked on one sheet ──
        void WriteGroups()
        {
            var ws = AddSheet(wb, "By Consultant & Manager");
            ws.Cell(1, 1).Value = $"Projected vs actual headcount by the people responsible for the stores — {monthName[snapMonth - 1]} {year}";
            ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
            ws.Cell(2, 1).Value = "Stores are grouped by the Store Reference entry for the period. Hiring need is an estimate (shortage + expected resignations).";
            ws.Cell(2, 1).Style.Font.Italic = true;
            string[] gh = { "Name", "Stores", "Projected", "Actual", "Gap", "Fill rate", "Shortage", "Expected resignations / month", "Hiring need (est.)", "Status" };
            var roles = new (string Title, Func<PlanningDetailRow, string> Pick)[]
            {
                ("Operation Consultants", x => x.OperationConsultant),
                ("Operation Directors", x => x.OperationDirector),
                ("Operation Managers", x => x.OperationManager),
                ("Senior Operation Consultants", x => x.SeniorOperationConsultant),
            };
            int rowNo = 4;
            foreach (var (title, pick) in roles)
            {
                ws.Cell(rowNo, 1).Value = title; ws.Cell(rowNo, 1).Style.Font.Bold = true; ws.Cell(rowNo, 1).Style.Font.FontSize = 12;
                rowNo++;
                for (int i = 0; i < gh.Length; i++)
                {
                    var c = ws.Cell(rowNo, i + 1);
                    c.Value = gh[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
                    c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                var first = rowNo + 1;
                rowNo++;
                var people = snap.Where(x => !string.IsNullOrWhiteSpace(pick(x)))
                    .GroupBy(x => pick(x).Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Stores = g.Select(x => x.Store).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        P = g.Sum(x => x.Projected), A = g.Sum(x => x.Actual ?? 0),
                        Attr = g.Sum(x => x.ExpectedAttrition ?? 0), Need = g.Sum(x => x.HiringNeed ?? 0),
                        Short = g.Sum(x => x.Shortage ?? 0.0),
                    })
                    .OrderBy(x => snapHasActual ? x.A - x.P : -x.P).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (people.Count == 0)
                {
                    ws.Cell(rowNo, 1).Value = "No data"; ws.Cell(rowNo, 1).Style.Font.Italic = true;
                    rowNo += 2;
                    continue;
                }
                foreach (var pe in people)
                {
                    ws.Cell(rowNo, 1).Value = SafeText(pe.Name);
                    SetIntCell(ws.Cell(rowNo, 2), pe.Stores);
                    SetIntCell(ws.Cell(rowNo, 3), pe.P);
                    if (snapHasActual)
                    {
                        SetIntCell(ws.Cell(rowNo, 4), pe.A);
                        ws.Cell(rowNo, 5).FormulaA1 = $"=D{rowNo}-C{rowNo}";
                        ws.Cell(rowNo, 6).FormulaA1 = $"=IF(C{rowNo}=0,\"\",D{rowNo}/C{rowNo})";
                        ws.Cell(rowNo, 6).Style.NumberFormat.Format = "0.0%";
                        SetIntCell(ws.Cell(rowNo, 7), (int)Math.Round(pe.Short, MidpointRounding.AwayFromZero));
                        ws.Cell(rowNo, 8).Value = Math.Round(pe.Attr, 1);
                        SetIntCell(ws.Cell(rowNo, 9), (int)Math.Round(pe.Need, MidpointRounding.AwayFromZero));
                        var fill = pe.P > 0 ? pe.A * 100.0 / pe.P : 100.0;
                        ws.Cell(rowNo, 10).Value = pe.P > 0 && pe.A > pe.P ? "Overstaffed" : fill >= 95 ? "On track" : fill >= 85 ? "Watch" : "Understaffed";
                    }
                    else ws.Cell(rowNo, 10).Value = "No actual data";
                    rowNo++;
                }
                var block = ws.Range(first - 1, 1, rowNo - 1, gh.Length);
                block.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                block.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                block.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor);
                block.Style.Border.InsideBorderColor = XLColor.FromHtml(GridColor);
                rowNo++; // blank line between the tables
            }
            ws.Columns().AdjustToContents(4, rowNo); // the tables only: the title and the note are longer than any column
            ws.Column(1).Width = Math.Max(ws.Column(1).Width, 32);
        }
        WriteGroups();

        // ── Hiring Plan: hires needed per store per month (whole year, same store/job/responsible filters) ──
        var forecast = await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od);
        if (forecast.Rows.Count > 0)
        {
            var hp = AddSheet(wb, "Hiring Plan");
            var heads = new List<string> { "Store", "Operation Consultant" };
            heads.AddRange(Enumerable.Range(0, 12).Select(i => monthName[i].Substring(0, 3) + (forecast.MonthModes[i] == "forecast" ? " (forecast)" : "")));
            heads.Add("Total");
            StyleHeader(hp, heads.ToArray());
            int hr = 2;
            foreach (var fr in forecast.Rows)
            {
                hp.Cell(hr, 1).Value = SafeText(fr.Store); hp.Cell(hr, 2).Value = SafeText(fr.OperationConsultant);
                for (int i = 0; i < 12; i++) if (forecast.MonthModes[i] != "none") SetIntCell(hp.Cell(hr, 3 + i), fr.Months[i]);
                hp.Cell(hr, 15).FormulaA1 = $"=SUM(C{hr}:N{hr})";
                hr++;
            }
            hp.Cell(hr, 1).Value = "Total";
            for (int c = 3; c <= 15; c++)
            {
                var col = hp.Cell(1, c).Address.ColumnLetter;
                hp.Cell(hr, c).FormulaA1 = $"=SUM({col}2:{col}{hr - 1})";
            }
            hp.Range(hr, 1, hr, 15).Style.Font.Bold = true;
            Finalize(hp);
            hp.Range(hr, 1, hr, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F3F3");
            hp.Range(2, 3, hr - 1, 14).AddConditionalFormat().ColorScale()
                .LowestValue(XLColor.White).HighestValue(XLColor.FromHtml("#F4A29B"));
            hp.SheetView.FreezeRows(1); hp.SheetView.FreezeColumns(2);
            hp.Cell(hr + 2, 1).Value = $"Hires needed per store. Months with an uploaded roster: max(0, projected − actual + expected resignations) per job. Later months are simulated from the {monthName[forecast.BaselineMonth - 1]} {forecast.BaselineYear} roster (expected resignations leave, hires fill up to the projection). Expected resignations already include people who leave in their first 90 days. An estimate, not a commitment.";
            hp.Cell(hr + 2, 1).Style.Font.Italic = true;
        }

        return wb;
    }

    // ── Hiring Forecast: hires needed per month ─────────────
    /// <summary>One matrix sheet: a name column (plus the consultant, or the store count), the 12 months and a total.
    /// Months with no projection stay blank; the totals row is live SUM formulas.</summary>
    private static void WriteForecastMatrix(XLWorkbook wb, string sheetName, string nameHeader, string? extraHeader, HiringForecastDto f,
        IEnumerable<HiringForecastRowDto> rows, Func<HiringForecastRowDto, string> extra, bool unassignedIfBlank = false)
    {
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        var ws = AddSheet(wb, sheetName);
        int x = extraHeader == null ? 0 : 1;          // extra column before the months
        int first = 2 + x, last = first + 11, totalCol = last + 1;
        var heads = new List<string> { nameHeader };
        if (extraHeader != null) heads.Add(extraHeader);
        heads.AddRange(Enumerable.Range(0, 12).Select(i => monthName[i].Substring(0, 3) + (f.MonthModes[i] == "forecast" ? " (forecast)" : "")));
        heads.Add("Total");
        StyleHeader(ws, heads.ToArray());
        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = SafeText(unassignedIfBlank && string.IsNullOrWhiteSpace(row.Store) ? "Unassigned" : row.Store);
            if (extraHeader != null) ws.Cell(r, 2).Value = SafeText(extra(row));
            for (int i = 0; i < 12; i++) if (f.MonthModes[i] != "none") SetIntCell(ws.Cell(r, first + i), row.Months[i]);
            ws.Cell(r, totalCol).FormulaA1 = $"=SUM({ws.Cell(r, first).Address.ColumnLetter}{r}:{ws.Cell(r, last).Address.ColumnLetter}{r})";
            r++;
        }
        Finalize(ws);
        int dataEnd = r - 1;
        ws.Cell(r, 1).Value = "Total";
        for (int c = first; c <= totalCol; c++)
        {
            var col = ws.Cell(1, c).Address.ColumnLetter;
            ws.Cell(r, c).FormulaA1 = $"=SUM({col}2:{col}{dataEnd})";
        }
        var totals = ws.Range(r, 1, r, totalCol);
        totals.Style.Font.Bold = true;
        totals.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F3F3");
        totals.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        totals.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor);
        if (dataEnd >= 2)
            ws.Range(2, first, dataEnd, last).AddConditionalFormat().ColorScale().LowestValue(XLColor.White).HighestValue(XLColor.FromHtml("#F4A29B"));
        ws.SheetView.FreezeColumns(1 + x);
    }

    /// <summary>Hiring Forecast report: hires needed per month for a year, by store, job, payroll group and
    /// responsible person. Same filters as the page; the forecast service serves everything from its cache.</summary>
    public async Task<XLWorkbook> BuildHiringForecastReportAsync(int year, string? store, string? jobs, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var byStore = await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od);
        var wb = new XLWorkbook();
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;

        var sum = AddSheet(wb, "Summary");
        sum.Cell(1, 1).Value = $"Hiring Forecast — Hires Needed per Month ({(byStore.Year > 0 ? byStore.Year : year)})";
        sum.Cell(1, 1).Style.Font.Bold = true; sum.Cell(1, 1).Style.Font.FontSize = 14;
        sum.Cell(2, 1).Value = "Store filter"; sum.Cell(2, 2).Value = string.IsNullOrWhiteSpace(store) ? "All accessible stores" : SafeText(store);
        sum.Cell(3, 1).Value = "Job filter"; sum.Cell(3, 2).Value = string.IsNullOrWhiteSpace(jobs) ? "All job titles" : SafeText(jobs);
        var responsible = string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(oc) ? null : "Consultant: " + oc,
            string.IsNullOrWhiteSpace(om) ? null : "Manager: " + om,
            string.IsNullOrWhiteSpace(soc) ? null : "Senior consultant: " + soc,
            string.IsNullOrWhiteSpace(od) ? null : "Director: " + od,
        }.Where(x => x != null));
        sum.Cell(4, 1).Value = "Responsible filter"; sum.Cell(4, 2).Value = responsible.Length == 0 ? "All" : SafeText(responsible);
        sum.Cell(5, 1).Value = "Generated"; sum.Cell(5, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        sum.Range(2, 1, 5, 1).Style.Font.Bold = true;
        sum.Range(1, 1, 1, 3).Merge();

        string? empty = !byStore.HasData ? "No job projections have been uploaded yet."
            : !byStore.HasRoster ? "No active-employee roster has been uploaded, so the forecast has nothing to start from."
            : byStore.Rows.Count == 0 ? "No hiring need matches the selected filters." : null;
        if (empty != null)
        {
            sum.Cell(7, 1).Value = empty;
            sum.Column(1).Width = 24;
            return wb;
        }

        sum.Cell(6, 1).Value = $"Hires needed per month. Months with an uploaded roster: max(0, projected − actual + expected resignations) per store and job. Later months are simulated from the {monthName[byStore.BaselineMonth - 1]} {byStore.BaselineYear} roster (expected resignations leave, hires fill up to the projection). Expected resignations already include people who leave in their first 90 days. Every sheet adds up to the same totals. An estimate, not a commitment.";
        sum.Cell(6, 1).Style.Font.Italic = true;
        sum.Range(6, 1, 6, 3).Merge().Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        sum.Row(6).Height = 78;

        const int hdr = 8;
        string[] mh = { "Month", "Source", "Hires needed" };
        for (int i = 0; i < mh.Length; i++)
        {
            var c = sum.Cell(hdr, i + 1);
            c.Value = mh[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
            c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        int rr = hdr + 1;
        for (int i = 0; i < 12; i++)
        {
            if (byStore.MonthModes[i] == "none") continue;
            sum.Cell(rr, 1).Value = monthName[i];
            sum.Cell(rr, 2).Value = byStore.MonthModes[i] == "forecast" ? "Forecast" : "Roster";
            SetIntCell(sum.Cell(rr, 3), byStore.MonthTotals[i]);
            rr++;
        }
        sum.Cell(rr, 1).Value = "Total";
        sum.Cell(rr, 3).FormulaA1 = $"=SUM(C{hdr + 1}:C{rr - 1})";
        sum.Range(rr, 1, rr, 3).Style.Font.Bold = true;
        sum.Range(hdr + 1, 1, rr, 3).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sum.Range(hdr + 1, 1, rr, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sum.Columns().AdjustToContents(hdr, rr);
        sum.Column(1).Width = Math.Max(sum.Column(1).Width, 22);
        sum.Column(2).Width = Math.Max(sum.Column(2).Width, 14);
        sum.Column(3).Width = Math.Max(sum.Column(3).Width, 16);

        WriteForecastMatrix(wb, "By Store", "Store", "Operation Consultant", byStore, byStore.Rows, r => r.OperationConsultant);
        var byJob = await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od, by: "job");
        WriteForecastMatrix(wb, "By Job", "Job title", null, byJob, byJob.Rows, _ => "");
        var byPayroll = await _planning.GetHiringForecastAsync(year, store, jobs, role, assignedName, om, oc, soc, od, by: "payroll");
        WriteForecastMatrix(wb, "By Payroll Group", "Payroll group", null, byPayroll, byPayroll.Rows, _ => "", unassignedIfBlank: true);

        // The four per-leader roll-ups, stacked on one sheet (same rounded store-month hires as the store sheet).
        var gs = AddSheet(wb, "By Consultant & Manager");
        gs.Cell(1, 1).Value = $"Hires needed per month by the people responsible for the stores — {byStore.Year}";
        gs.Cell(1, 1).Style.Font.Bold = true; gs.Cell(1, 1).Style.Font.FontSize = 13;
        var heads = new List<string> { "Name", "Stores" };
        heads.AddRange(Enumerable.Range(0, 12).Select(i => monthName[i].Substring(0, 3) + (byStore.MonthModes[i] == "forecast" ? " (forecast)" : "")));
        heads.Add("Total");
        var groups = new (string Title, List<HiringForecastRowDto> Rows)[]
        {
            ("Operation Consultants", byStore.ByOperationConsultant),
            ("Operation Directors", byStore.ByOperationDirector),
            ("Operation Managers", byStore.ByOperationManager),
            ("Senior Operation Consultants", byStore.BySeniorOperationConsultant),
        };
        int gr = 3;
        foreach (var (title, list) in groups)
        {
            gs.Cell(gr, 1).Value = title; gs.Cell(gr, 1).Style.Font.Bold = true; gs.Cell(gr, 1).Style.Font.FontSize = 12;
            gr++;
            for (int i = 0; i < heads.Count; i++)
            {
                var c = gs.Cell(gr, i + 1);
                c.Value = heads[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
                c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            int headRow = gr; gr++;
            if (list.Count == 0) { gs.Cell(gr, 1).Value = "No data"; gs.Cell(gr, 1).Style.Font.Italic = true; gr += 2; continue; }
            foreach (var row in list)
            {
                gs.Cell(gr, 1).Value = SafeText(row.Store);
                SetIntCell(gs.Cell(gr, 2), row.StoreCount);
                for (int i = 0; i < 12; i++) if (byStore.MonthModes[i] != "none") SetIntCell(gs.Cell(gr, 3 + i), row.Months[i]);
                gs.Cell(gr, 15).FormulaA1 = $"=SUM(C{gr}:N{gr})";
                gr++;
            }
            var block = gs.Range(headRow, 1, gr - 1, heads.Count);
            block.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; block.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            block.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor); block.Style.Border.InsideBorderColor = XLColor.FromHtml(GridColor);
            gr++;
        }
        gs.Columns().AdjustToContents(3, gr);
        gs.Column(1).Width = Math.Max(gs.Column(1).Width, 32);
        return wb;
    }

    // ── Crew Trainers: trainers against the plan and the 1-per-6 rule ─────
    /// <summary>Crew Trainers report for one month that has a trainer list: trainers against the Crew Trainer projection
    /// and against the 1-trainer-per-6-crew rule, by store and by leader, the year's trend and who joined or left the list.</summary>
    public async Task<XLWorkbook> BuildCrewTrainerReportAsync(int year, int month, string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        if (_trainers == null) throw new InvalidOperationException("The Crew Trainers service is not available.");
        var dto = await _trainers.GetAsync(year > 0 ? year : null, month > 0 ? month : null, store, role, assignedName, om, oc, soc, od);
        var wb = new XLWorkbook();
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;

        var sum = AddSheet(wb, "Summary");
        sum.Cell(1, 1).Value = dto.HasData ? $"Crew Trainers — {monthName[dto.Month - 1]} {dto.Year}" : "Crew Trainers";
        sum.Cell(1, 1).Style.Font.Bold = true; sum.Cell(1, 1).Style.Font.FontSize = 14;
        sum.Cell(2, 1).Value = "Store filter"; sum.Cell(2, 2).Value = string.IsNullOrWhiteSpace(store) ? "All accessible stores" : SafeText(store);
        var responsible = string.Join("; ", new[]
        {
            string.IsNullOrWhiteSpace(oc) ? null : "Consultant: " + oc,
            string.IsNullOrWhiteSpace(om) ? null : "Manager: " + om,
            string.IsNullOrWhiteSpace(soc) ? null : "Senior consultant: " + soc,
            string.IsNullOrWhiteSpace(od) ? null : "Director: " + od,
        }.Where(x => x != null));
        sum.Cell(3, 1).Value = "Responsible filter"; sum.Cell(3, 2).Value = responsible.Length == 0 ? "All" : SafeText(responsible);
        sum.Cell(4, 1).Value = "Generated"; sum.Cell(4, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        sum.Range(2, 1, 4, 1).Style.Font.Bold = true;
        sum.Range(1, 1, 1, 3).Merge();

        if (!dto.HasData)
        {
            sum.Cell(6, 1).Value = "No crew trainer list has been uploaded yet.";
            sum.Column(1).Width = 24;
            return wb;
        }

        sum.Cell(5, 1).Value = $"Trainers are the people on the uploaded trainer list. Plan = Crew Trainer + Hourly Paid Crew Trainer in the projection. Required = crew-level employees (trainers excluded) ÷ {dto.CrewPerTrainer}, rounded to the nearest whole number. Gap = Actual − Plan or Actual − Required (negative = short). Only stores in the selected month's projection are compared.";
        sum.Cell(5, 1).Style.Font.Italic = true;
        sum.Range(5, 1, 5, 3).Merge().Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        sum.Row(5).Height = 64;

        const int hdr = 7;
        string[] kh = { "Measure", "Value" };
        for (int i = 0; i < kh.Length; i++)
        {
            var c = sum.Cell(hdr, i + 1);
            c.Value = kh[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
            c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        var k = dto.Kpis;
        var measures = new List<(string Label, int Value)>
        {
            ("Trainers in the plan", k.Projected), ("Trainers on the list", k.Actual), ("Gap vs plan (actual − plan)", k.Gap),
        };
        if (dto.HasCrewLevelJobs)
        {
            measures.Add(("Crew-level employees", k.CrewLevel));
            measures.Add(($"Trainers required (1 per {dto.CrewPerTrainer})", k.Required));
            measures.Add(("Gap vs rule (actual − required)", k.GapRule));
        }
        measures.Add(($"Trainers who resigned (last {k.LookbackMonths} months)", k.Resigned));
        int kr = hdr + 1;
        foreach (var (label, value) in measures) { sum.Cell(kr, 1).Value = label; SetIntCell(sum.Cell(kr, 2), value); kr++; }
        sum.Range(hdr + 1, 1, kr - 1, 2).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sum.Range(hdr + 1, 1, kr - 1, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sum.Columns().AdjustToContents(hdr, kr - 1);
        sum.Column(1).Width = Math.Max(sum.Column(1).Width, 36);
        sum.Column(2).Width = Math.Max(sum.Column(2).Width, 14);

        // By Store: trainers against the plan and against the rule, with the store's leaders.
        var bs = AddSheet(wb, "By Store");
        StyleHeader(bs, new[] { "Store", "Operation Consultant", "Operation Manager", "Senior Operation Consultant", "Operation Director",
            "Plan", "Trainers", "Gap vs plan", "Crew level", "Required", "Gap vs rule" });
        int sr = 2;
        foreach (var s in dto.ByStore)
        {
            bs.Cell(sr, 1).Value = SafeText(s.Store); bs.Cell(sr, 2).Value = SafeText(s.OperationConsultant); bs.Cell(sr, 3).Value = SafeText(s.OperationManager);
            bs.Cell(sr, 4).Value = SafeText(s.SeniorOperationConsultant); bs.Cell(sr, 5).Value = SafeText(s.OperationDirector);
            SetIntCell(bs.Cell(sr, 6), s.Projected); SetIntCell(bs.Cell(sr, 7), s.Actual);
            bs.Cell(sr, 8).FormulaA1 = $"=G{sr}-F{sr}";
            SetIntCell(bs.Cell(sr, 9), s.CrewLevel); SetIntCell(bs.Cell(sr, 10), s.Required);
            bs.Cell(sr, 11).FormulaA1 = $"=G{sr}-J{sr}";
            sr++;
        }
        Finalize(bs);
        bs.SheetView.FreezeColumns(1);

        // By leader: the store rows added up under each store's leader, four tables on one sheet.
        var gs = AddSheet(wb, "By Consultant & Manager");
        gs.Cell(1, 1).Value = $"Crew trainers by the people responsible for the stores — {monthName[dto.Month - 1]} {dto.Year}";
        gs.Cell(1, 1).Style.Font.Bold = true; gs.Cell(1, 1).Style.Font.FontSize = 13;
        string[] gh = { "Name", "Stores", "Plan", "Trainers", "Gap vs plan", "Crew level", "Required", "Gap vs rule" };
        var groups = new (string Title, List<CrewTrainerGroupDto> Rows)[]
        {
            ("Operation Consultants", dto.ByOperationConsultant), ("Operation Directors", dto.ByOperationDirector),
            ("Operation Managers", dto.ByOperationManager), ("Senior Operation Consultants", dto.BySeniorOperationConsultant),
        };
        int gr = 3;
        foreach (var (title, list) in groups)
        {
            gs.Cell(gr, 1).Value = title; gs.Cell(gr, 1).Style.Font.Bold = true; gs.Cell(gr, 1).Style.Font.FontSize = 12;
            gr++;
            for (int i = 0; i < gh.Length; i++)
            {
                var c = gs.Cell(gr, i + 1);
                c.Value = gh[i]; c.Style.Font.Bold = true; c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
                c.Style.Font.FontColor = XLColor.White; c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            int headRow = gr; gr++;
            if (list.Count == 0) { gs.Cell(gr, 1).Value = "No data"; gs.Cell(gr, 1).Style.Font.Italic = true; gr += 2; continue; }
            foreach (var g in list)
            {
                gs.Cell(gr, 1).Value = SafeText(g.Name);
                SetIntCell(gs.Cell(gr, 2), g.StoreCount); SetIntCell(gs.Cell(gr, 3), g.Projected); SetIntCell(gs.Cell(gr, 4), g.Actual);
                gs.Cell(gr, 5).FormulaA1 = $"=D{gr}-C{gr}";
                SetIntCell(gs.Cell(gr, 6), g.CrewLevel); SetIntCell(gs.Cell(gr, 7), g.Required);
                gs.Cell(gr, 8).FormulaA1 = $"=D{gr}-G{gr}";
                gr++;
            }
            var block = gs.Range(headRow, 1, gr - 1, gh.Length);
            block.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; block.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            block.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor); block.Style.Border.InsideBorderColor = XLColor.FromHtml(GridColor);
            gr++;
        }
        gs.Columns().AdjustToContents(3, gr);
        gs.Column(1).Width = Math.Max(gs.Column(1).Width, 32);

        // Trend over the year: trainers in the plan against trainers on the list.
        var tr = AddSheet(wb, "Trend");
        StyleHeader(tr, new[] { "Month", "Plan", "Trainers", "Gap vs plan" });
        int tr2 = 2;
        foreach (var t in dto.Trend.OrderBy(t => t.Month))
        {
            tr.Cell(tr2, 1).Value = monthName[t.Month - 1];
            SetIntCell(tr.Cell(tr2, 2), t.Projected);
            if (t.Actual.HasValue)
            {
                SetIntCell(tr.Cell(tr2, 3), t.Actual.Value);
                tr.Cell(tr2, 4).FormulaA1 = $"=C{tr2}-B{tr2}";
            }
            tr2++;
        }
        Finalize(tr);

        // Joined / left the list since the previous one.
        if (dto.HasPrevious)
        {
            var mv = AddSheet(wb, "Joined & Left");
            StyleHeader(mv, new[] { "Change", "Employee ID", "Name", "Store", "Status" });
            int mr = 2;
            string Status(string s) => s switch { "resigned" => "Resigned", "active" => "Still active (off the list)", "gone" => "Not on the roster", _ => "" };
            foreach (var e in dto.Entered) { mv.Cell(mr, 1).Value = "Joined the list"; mv.Cell(mr, 2).Value = SafeText(e.EmployeeId); mv.Cell(mr, 3).Value = SafeText(e.Name); mv.Cell(mr, 4).Value = SafeText(e.Store); mr++; }
            foreach (var e in dto.Left) { mv.Cell(mr, 1).Value = "Left the list"; mv.Cell(mr, 2).Value = SafeText(e.EmployeeId); mv.Cell(mr, 3).Value = SafeText(e.Name); mv.Cell(mr, 4).Value = SafeText(e.Store); mv.Cell(mr, 5).Value = Status(e.Status); mr++; }
            Finalize(mv);
            mv.Cell(mr + 1, 1).Value = $"Compared with the {monthName[dto.PreviousMonth - 1]} {dto.PreviousYear} trainer list.";
            mv.Cell(mr + 1, 1).Style.Font.Italic = true;
        }
        return wb;
    }

    // ── OC/OM Comparison ─────────────────────────────────────
    private static void WriteOcOmSheet(XLWorkbook wb, string sheetName, string nameHeader, List<OcOmRow> rows, string period)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { nameHeader, "Period", "Stores", "Headcount", "Resignations", "Avg. Turnover Rate" });
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = SafeText(r.Name);
            ws.Cell(i + 2, 2).Value = period;
            SetIntCell(ws.Cell(i + 2, 3), r.StoreCount);
            SetIntCell(ws.Cell(i + 2, 4), r.TotalHeadcount);
            SetIntCell(ws.Cell(i + 2, 5), r.TotalResignations);
            SetPercentCell(ws.Cell(i + 2, 6), r.AvgTurnoverRate);
        }
        Finalize(ws);
    }

    private async Task AddOcOmComparisonSheetsAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var result = await _dashboard.GetOcOmAnalysisAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        var period = (fromMonth.HasValue && fromYear.HasValue && (fromMonth != month || fromYear != year))
            ? $"{fromMonth:D2}-{fromYear} to {month:D2}-{year}"
            : $"{month:D2}-{year}";
        WriteOcOmSheet(wb, "By Operation Consultant", "Operation Consultant", result.OcRows, period);
        WriteOcOmSheet(wb, "By Operation Manager", "Operation Manager", result.OmRows, period);
        WriteOcOmSheet(wb, "By Senior Op. Consultant", "Senior Operation Consultant", result.SocRows, period);
        WriteOcOmSheet(wb, "By Operation Director", "Operation Director", result.OdRows, period);
    }

    public async Task<XLWorkbook> BuildOcOmComparisonReportAsync(int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var wb = new XLWorkbook();
        await AddOcOmComparisonSheetsAsync(wb, month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        return wb;
    }

    // ── Comparison (Period A vs Period B) ─────────────────────
    private class ComparisonSide
    {
        public int Year;
        public string? Months;
        public string? Store;
        public string? Om, Oc, Soc, Od;
        public string Label = "";
        public int AnchorMonth;
    }

    /// <summary>Resolves both sides' Year/Months exactly like the Comparisons
    /// dashboard page's own cmpInit(): Side A defaults to the latest available
    /// year with all its months; Side B defaults to the same months one year
    /// earlier (falling back to the earliest available year if there's no data
    /// a year before A). Only the Year/Months are ever defaulted — an explicit
    /// store/OM/OC/SOC/OD filter is always honored as given (null = unfiltered).</summary>
    private async Task<(ComparisonSide A, ComparisonSide B)> ResolveComparisonSidesAsync(
        int? yearA, string? monthsA, string? storeA, string? omA, string? ocA, string? socA, string? odA,
        int? yearB, string? monthsB, string? storeB, string? omB, string? ocB, string? socB, string? odB)
    {
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        var yearMonths = periods.GroupBy(p => p.Year).ToDictionary(g => g.Key, g => g.Select(p => p.Month).OrderBy(m => m).ToList());
        var years = yearMonths.Keys.OrderByDescending(y => y).ToList();

        var a = new ComparisonSide { Store = storeA, Om = omA, Oc = ocA, Soc = socA, Od = odA };
        var b = new ComparisonSide { Store = storeB, Om = omB, Oc = ocB, Soc = socB, Od = odB };

        if (yearA.HasValue) { a.Year = yearA.Value; a.Months = monthsA; }
        else if (years.Count > 0)
        {
            a.Year = years[0];
            a.Months = string.Join(",", yearMonths[a.Year]);
        }

        if (yearB.HasValue) { b.Year = yearB.Value; b.Months = monthsB; }
        else if (years.Count > 0)
        {
            var aMonthList = MultiValueFilter.Split(a.Months)?.Select(int.Parse).ToList() ?? yearMonths.GetValueOrDefault(a.Year, new List<int>());
            var prevYear = a.Year - 1;
            if (yearMonths.TryGetValue(prevYear, out var prevMonths))
            {
                b.Year = prevYear;
                b.Months = string.Join(",", aMonthList.Where(prevMonths.Contains));
            }
            else
            {
                b.Year = years[^1];
                b.Months = string.Join(",", yearMonths[b.Year]);
            }
        }

        foreach (var side in new[] { a, b })
        {
            var monthList = MultiValueFilter.Split(side.Months)?.Select(int.Parse).ToList();
            side.AnchorMonth = monthList is { Count: > 0 } ? monthList.Max() : DateTime.Now.Month;
            side.Label = DescribePeriodScope(side.Months, side.Year);
        }

        return (a, b);
    }

    private async Task<(int Headcount, int NewHires, int Resignations, double TurnoverRate, int NinetyHires, int NinetyEarly, double NinetyRate)> LoadComparisonKpisAsync(
        ComparisonSide side, string role, string? assignedName)
    {
        var kpi = await _dashboard.GetKpisAsync(side.AnchorMonth, side.Year, side.Store, role, assignedName,
            om: side.Om, oc: side.Oc, soc: side.Soc, od: side.Od, months: side.Months);
        var ninety = await _ninetyDay.GetKpiAsync(side.AnchorMonth, side.Year, side.Store, role, assignedName,
            om: side.Om, oc: side.Oc, soc: side.Soc, od: side.Od, months: side.Months);
        return (kpi.TotalHeadcount, kpi.NewHires, kpi.TotalResignations, kpi.TurnoverRate, ninety.TotalHires, ninety.EarlyLeavers, ninety.Rate);
    }

    private static void WriteKpiCompareRow(IXLWorksheet ws, int row, string metric, double a, double b, bool asPercent)
    {
        ws.Cell(row, 1).Value = metric;
        if (asPercent) { SetPercentCell(ws.Cell(row, 2), a); SetPercentCell(ws.Cell(row, 3), b); SetPercentCell(ws.Cell(row, 4), a - b); }
        else { SetIntCell(ws.Cell(row, 2), (int)a); SetIntCell(ws.Cell(row, 3), (int)b); SetIntCell(ws.Cell(row, 4), (int)(a - b)); }
    }

    public async Task<XLWorkbook> BuildComparisonReportAsync(string role, string? assignedName,
        int? yearA = null, string? monthsA = null, string? storeA = null, string? omA = null, string? ocA = null, string? socA = null, string? odA = null,
        int? yearB = null, string? monthsB = null, string? storeB = null, string? omB = null, string? ocB = null, string? socB = null, string? odB = null)
    {
        var wb = new XLWorkbook();
        var (a, b) = await ResolveComparisonSidesAsync(yearA, monthsA, storeA, omA, ocA, socA, odA, yearB, monthsB, storeB, omB, ocB, socB, odB);

        var kpiA = await LoadComparisonKpisAsync(a, role, assignedName);
        var kpiB = await LoadComparisonKpisAsync(b, role, assignedName);

        var wsKpi = AddSheet(wb, "Comparison KPIs");
        StyleHeader(wsKpi, new[] { "Metric", $"Period A ({a.Label})", $"Period B ({b.Label})", "Delta (A − B)" });
        WriteKpiCompareRow(wsKpi, 2, "Total Headcount", kpiA.Headcount, kpiB.Headcount, false);
        WriteKpiCompareRow(wsKpi, 3, "New Hires", kpiA.NewHires, kpiB.NewHires, false);
        WriteKpiCompareRow(wsKpi, 4, "Resignations", kpiA.Resignations, kpiB.Resignations, false);
        WriteKpiCompareRow(wsKpi, 5, "Turnover Rate", kpiA.TurnoverRate, kpiB.TurnoverRate, true);
        WriteKpiCompareRow(wsKpi, 6, "90-Day Total Hires", kpiA.NinetyHires, kpiB.NinetyHires, false);
        WriteKpiCompareRow(wsKpi, 7, "90-Day Early Leavers", kpiA.NinetyEarly, kpiB.NinetyEarly, false);
        WriteKpiCompareRow(wsKpi, 8, "90-Day Early Leave Rate", kpiA.NinetyRate, kpiB.NinetyRate, true);
        Finalize(wsKpi);

        var leadership = await BuildLeadershipMapAsync(role, assignedName);

        var turnoverA = await _dashboard.GetStoreComparisonAsync(a.AnchorMonth, a.Year, role, assignedName, om: a.Om, oc: a.Oc, soc: a.Soc, od: a.Od, months: a.Months);
        var turnoverB = await _dashboard.GetStoreComparisonAsync(b.AnchorMonth, b.Year, role, assignedName, om: b.Om, oc: b.Oc, soc: b.Soc, od: b.Od, months: b.Months);
        var turnoverBByStore = turnoverB.ToDictionary(r => r.StoreName, r => r.TurnoverRate);
        var allTurnoverStores = turnoverA.Select(r => r.StoreName).Union(turnoverBByStore.Keys).OrderBy(s => s).ToList();
        var turnoverAByStore = turnoverA.ToDictionary(r => r.StoreName, r => r.TurnoverRate);

        var wsTurnover = AddSheet(wb, "Turnover By Store (A vs B)");
        StyleHeader(wsTurnover, new[] { "Store" }.Concat(LeadershipHeaders)
            .Concat(new[] { $"Turnover Rate A ({a.Label})", $"Turnover Rate B ({b.Label})", "Delta (A − B)" }).ToArray());
        for (int i = 0; i < allTurnoverStores.Count; i++)
        {
            var store = allTurnoverStores[i];
            wsTurnover.Cell(i + 2, 1).Value = SafeText(store);
            int col = WriteLeadershipCells(wsTurnover, i + 2, 2, leadership, store);
            var rateA = turnoverAByStore.GetValueOrDefault(store);
            var rateB = turnoverBByStore.GetValueOrDefault(store);
            SetPercentCell(wsTurnover.Cell(i + 2, col), rateA);
            SetPercentCell(wsTurnover.Cell(i + 2, col + 1), rateB);
            SetPercentCell(wsTurnover.Cell(i + 2, col + 2), rateA - rateB);
        }
        Finalize(wsTurnover);

        var ninetyA = await _ninetyDay.GetByStoreAsync(a.AnchorMonth, a.Year, role, assignedName, om: a.Om, oc: a.Oc, soc: a.Soc, od: a.Od, months: a.Months);
        var ninetyB = await _ninetyDay.GetByStoreAsync(b.AnchorMonth, b.Year, role, assignedName, om: b.Om, oc: b.Oc, soc: b.Soc, od: b.Od, months: b.Months);
        var ninetyBByStore = ninetyB.ToDictionary(r => r.Label, r => r.Value);
        var ninetyAByStore = ninetyA.ToDictionary(r => r.Label, r => r.Value);
        var allNinetyStores = ninetyAByStore.Keys.Union(ninetyBByStore.Keys).OrderBy(s => s).ToList();

        var wsNinety = AddSheet(wb, "90-Day By Store (A vs B)");
        StyleHeader(wsNinety, new[] { "Store" }.Concat(LeadershipHeaders)
            .Concat(new[] { $"90-Day Rate A ({a.Label})", $"90-Day Rate B ({b.Label})", "Delta (A − B)" }).ToArray());
        for (int i = 0; i < allNinetyStores.Count; i++)
        {
            var store = allNinetyStores[i];
            wsNinety.Cell(i + 2, 1).Value = SafeText(store);
            int col = WriteLeadershipCells(wsNinety, i + 2, 2, leadership, store);
            var rateA = ninetyAByStore.GetValueOrDefault(store);
            var rateB = ninetyBByStore.GetValueOrDefault(store);
            SetPercentCell(wsNinety.Cell(i + 2, col), rateA);
            SetPercentCell(wsNinety.Cell(i + 2, col + 1), rateB);
            SetPercentCell(wsNinety.Cell(i + 2, col + 2), rateA - rateB);
        }
        Finalize(wsNinety);

        return wb;
    }
}
