using System.Globalization;
using ClosedXML.Excel;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// The Comparison report's sheets that mirror the tabs of the Comparisons page (Trend, Teams, Composition, Cohorts,
/// Team Tenure, Experience, Supervisors, Leaders…). They follow the page's rules: counts become monthly averages when the
/// two sides cover a different number of months, exit answers are shares of each side's own answers, and Retention data is
/// a snapshot at each side's last month.
/// </summary>
public partial class ReportService
{
    private enum PairMode { Count, Share, Percent }

    /// <summary>One row of a "Section | Item | A | B | Delta" sheet.</summary>
    private sealed record PairLine(string Section, string Item, double? A, double? B, bool Percent);

    private static string MonthName(int month) =>
        month is >= 1 and <= 12 ? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month) : month.ToString();

    /// <summary>Rows by name; two rows sharing a name (two leaders called the same) keep the first instead of failing the export.</summary>
    private static Dictionary<string, T> ByName<T>(IEnumerable<T> rows, Func<T, string> name) =>
        rows.GroupBy(name).ToDictionary(g => g.Key, g => g.First());

    private static void PutText(IXLWorksheet ws, int row, int col, string? text) => ws.Cell(row, col).Value = SafeText(text);

    private static void PutCount(IXLWorksheet ws, int row, int col, double? value, bool fractional)
    {
        if (value.HasValue) SetCountCell(ws.Cell(row, col), value.Value, fractional);
        else ws.Cell(row, col).Value = "—";
    }

    private static void PutPercent(IXLWorksheet ws, int row, int col, double? value) => SetNullablePercentCell(ws.Cell(row, col), value);

    private static double? Diff(double? a, double? b) => a.HasValue && b.HasValue ? a.Value - b.Value : null;

    private async Task AddComparisonTabSheetsAsync(XLWorkbook wb, string topic, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        switch (topic)
        {
            case ComparisonTopics.Turnover: await AddTurnoverTabsAsync(wb, a, b, role, assignedName); break;
            case ComparisonTopics.Workforce: await AddWorkforceTabsAsync(wb, a, b, role, assignedName); break;
            case ComparisonTopics.Ninety: await AddNinetyTabsAsync(wb, a, b, role, assignedName); break;
            case ComparisonTopics.Retention: await AddRetentionTabsAsync(wb, a, b, role, assignedName); break;
            case ComparisonTopics.Exit: await AddExitTabsAsync(wb, a, b, role, assignedName); break;
            case ComparisonTopics.Scorecard: await AddScorecardTabsAsync(wb, a, b, role, assignedName); break;
        }
    }

    // ── shared writers ────────────────────────────────────────────────────────

    /// <summary>A and B lists side by side. Count = monthly average when the sides differ in length, Share = percent of
    /// each side's own total, Percent = values that already are percentages.</summary>
    private static List<PairLine> PairLines(string section, IEnumerable<ChartDataItem> rowsA, IEnumerable<ChartDataItem> rowsB,
        ComparisonSide a, ComparisonSide b, PairMode mode, bool keepOrder = false)
    {
        var la = rowsA.ToList();
        var lb = rowsB.ToList();
        var uneven = MonthCount(a) != MonthCount(b);

        Dictionary<string, double> Fold(List<ChartDataItem> rows, ComparisonSide side)
        {
            var total = rows.Sum(r => (double)r.Value);
            return rows.GroupBy(r => BlankAs(r.Label)).ToDictionary(g => g.Key, g => g.Sum(r =>
                mode == PairMode.Share ? (total > 0 ? r.Value * 100.0 / total : 0.0)
                : mode == PairMode.Count ? PerMonth(side, r.Value, uneven)
                : (double)r.Value));
        }

        var ma = Fold(la, a);
        var mb = Fold(lb, b);
        var labels = la.Select(r => BlankAs(r.Label)).Concat(lb.Select(r => BlankAs(r.Label))).Distinct().ToList();
        if (!keepOrder)
            labels = labels.OrderByDescending(l => ma.GetValueOrDefault(l) + mb.GetValueOrDefault(l)).ThenBy(l => l).ToList();
        return labels.Select(l => new PairLine(section, l, ma.GetValueOrDefault(l), mb.GetValueOrDefault(l), mode != PairMode.Count)).ToList();
    }

    /// <summary>"Section | Item | A | B | Delta (A − B)".</summary>
    private static void WriteSectionSheet(XLWorkbook wb, string sheetName, string sectionHeader, string itemHeader, ComparisonSide a, ComparisonSide b,
        IReadOnlyList<PairLine> lines, bool fractionalCounts, params string?[] notes)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { sectionHeader, itemHeader, $"Period A ({a.Label})", $"Period B ({b.Label})", "Delta (A − B)" });
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var row = i + 2;
            PutText(ws, row, 1, line.Section);
            PutText(ws, row, 2, line.Item);
            if (line.Percent)
            {
                PutPercent(ws, row, 3, line.A);
                PutPercent(ws, row, 4, line.B);
                PutPercent(ws, row, 5, Diff(line.A, line.B));
            }
            else
            {
                PutCount(ws, row, 3, line.A, fractionalCounts);
                PutCount(ws, row, 4, line.B, fractionalCounts);
                PutCount(ws, row, 5, Diff(line.A, line.B), fractionalCounts);
            }
        }
        Finalize(ws);
        AddComparisonNotes(ws, notes);
    }

    /// <summary>Month-by-month metrics of both sides: "Month | metric A | metric B | Delta | …".</summary>
    private static void WriteMonthlySheet(XLWorkbook wb, string sheetName, ComparisonSide a, ComparisonSide b,
        IReadOnlyList<(int Month, double[] Values)> seriesA, IReadOnlyList<(int Month, double[] Values)> seriesB,
        IReadOnlyList<(string Name, bool Percent)> metrics, params string?[] notes)
    {
        var months = seriesA.Select(p => p.Month).Concat(seriesB.Select(p => p.Month)).Distinct().OrderBy(m => m).ToList();
        var ws = AddSheet(wb, sheetName);
        var headers = new List<string> { "Month" };
        foreach (var m in metrics) { headers.Add($"{m.Name} A"); headers.Add($"{m.Name} B"); headers.Add("Delta (A − B)"); }
        StyleHeader(ws, headers.ToArray());
        for (var i = 0; i < months.Count; i++)
        {
            var row = i + 2;
            PutText(ws, row, 1, MonthName(months[i]));
            var pa = seriesA.Where(p => p.Month == months[i]).Select(p => p.Values).FirstOrDefault();
            var pb = seriesB.Where(p => p.Month == months[i]).Select(p => p.Values).FirstOrDefault();
            for (var k = 0; k < metrics.Count; k++)
            {
                double? va = pa == null ? null : pa[k];
                double? vb = pb == null ? null : pb[k];
                var col = 2 + k * 3;
                if (metrics[k].Percent)
                {
                    PutPercent(ws, row, col, va); PutPercent(ws, row, col + 1, vb); PutPercent(ws, row, col + 2, Diff(va, vb));
                }
                else
                {
                    PutCount(ws, row, col, va, false); PutCount(ws, row, col + 1, vb, false); PutCount(ws, row, col + 2, Diff(va, vb), false);
                }
            }
        }
        Finalize(ws);
        AddComparisonNotes(ws, notes);
    }

    private Task<List<MonthlyPoint>> LoadMonthlySeriesAsync(ComparisonSide s, string role, string? assignedName) =>
        _dashboard.GetMonthlySeriesAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName,
            om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months, jobTitles: s.Job);

    private static readonly string[] TeamLevels = { "Operation Consultant", "Operation Manager", "Senior Operation Consultant", "Operation Director" };

    /// <summary>OC / OM / SOC / OD rows of the dashboard analysis (turnover: resignations + rate; workforce: headcount + rate).</summary>
    private async Task AddDashboardTeamsSheetAsync(XLWorkbook wb, string sheetName, ComparisonSide a, ComparisonSide b, string role, string? assignedName, bool workforce)
    {
        var oa = await _dashboard.GetOcOmAnalysisAsync(a.AnchorMonth, a.Year, role, assignedName,
            om: a.Om, oc: a.Oc, soc: a.Soc, od: a.Od, months: a.Months, jobTitles: a.Job);
        var ob = await _dashboard.GetOcOmAnalysisAsync(b.AnchorMonth, b.Year, role, assignedName,
            om: b.Om, oc: b.Oc, soc: b.Soc, od: b.Od, months: b.Months, jobTitles: b.Job);
        var uneven = MonthCount(a) != MonthCount(b);

        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, workforce
            ? new[] { "Level", "Name", "Stores", "Headcount A", "Headcount B", "Delta (A − B)", "Turnover Rate A", "Turnover Rate B" }
            : new[] { "Level", "Name", "Stores", "Resignations A", "Resignations B", "Turnover Rate A", "Turnover Rate B", "Delta (A − B)" });

        var levels = new[]
        {
            (TeamLevels[0], oa.OcRows, ob.OcRows), (TeamLevels[1], oa.OmRows, ob.OmRows),
            (TeamLevels[2], oa.SocRows, ob.SocRows), (TeamLevels[3], oa.OdRows, ob.OdRows),
        };
        var row = 2;
        foreach (var (level, rowsA, rowsB) in levels)
        {
            var ma = ByName(rowsA, r => r.Name);
            var mb = ByName(rowsB, r => r.Name);
            var names = ma.Keys.Union(mb.Keys)
                .OrderByDescending(n => (ma.GetValueOrDefault(n)?.AvgTurnoverRate ?? 0) + (mb.GetValueOrDefault(n)?.AvgTurnoverRate ?? 0))
                .ThenBy(n => n).ToList();
            foreach (var name in names)
            {
                ma.TryGetValue(name, out var ra);
                mb.TryGetValue(name, out var rb);
                PutText(ws, row, 1, level);
                PutText(ws, row, 2, name);
                SetIntCell(ws.Cell(row, 3), (ra ?? rb)!.StoreCount);
                double? turnoverA = ra?.AvgTurnoverRate, turnoverB = rb?.AvgTurnoverRate;
                if (workforce)
                {
                    double? ha = ra == null ? null : PerMonth(a, ra.TotalHeadcount, uneven);
                    double? hb = rb == null ? null : PerMonth(b, rb.TotalHeadcount, uneven);
                    PutCount(ws, row, 4, ha, uneven); PutCount(ws, row, 5, hb, uneven); PutCount(ws, row, 6, Diff(ha, hb), uneven);
                    PutPercent(ws, row, 7, turnoverA); PutPercent(ws, row, 8, turnoverB);
                }
                else
                {
                    double? xa = ra == null ? null : PerMonth(a, ra.TotalResignations, uneven);
                    double? xb = rb == null ? null : PerMonth(b, rb.TotalResignations, uneven);
                    PutCount(ws, row, 4, xa, uneven); PutCount(ws, row, 5, xb, uneven);
                    PutPercent(ws, row, 6, turnoverA); PutPercent(ws, row, 7, turnoverB); PutPercent(ws, row, 8, Diff(turnoverA, turnoverB));
                }
                row++;
            }
        }
        Finalize(ws);
        AddComparisonNotes(ws, UnevenNote(a, b, "counts"));
    }

    // ── Turnover ──────────────────────────────────────────────────────────────
    private async Task AddTurnoverTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        var sa = await LoadMonthlySeriesAsync(a, role, assignedName);
        var sb = await LoadMonthlySeriesAsync(b, role, assignedName);
        (int, double[]) Point(MonthlyPoint p) => (p.Month, new[] { p.TurnoverRate, (double)p.Resignations, (double)p.NewHires });
        WriteMonthlySheet(wb, "Monthly Trend", a, b, sa.Select(Point).ToList(), sb.Select(Point).ToList(),
            new[] { ("Turnover Rate", true), ("Resignations", false), ("New Hires", false) },
            "Each month on its own (not averaged), for the months of each side.");

        await AddDashboardTeamsSheetAsync(wb, "Teams (A vs B)", a, b, role, assignedName, workforce: false);

        if (a.Store != null || b.Store != null) return;   // the store movers need both sides to cover all stores
        var storesA = (await LoadComparisonStoresAsync(a, role, assignedName)).ToDictionary(r => r.StoreName, r => r.TurnoverRate);
        var storesB = (await LoadComparisonStoresAsync(b, role, assignedName)).ToDictionary(r => r.StoreName, r => r.TurnoverRate);
        var moved = storesA.Where(kv => storesB.ContainsKey(kv.Key)).Select(kv => (Store: kv.Key, A: kv.Value, B: storesB[kv.Key], D: kv.Value - storesB[kv.Key])).ToList();
        var lines = new List<PairLine>();
        lines.AddRange(moved.Where(m => m.D < 0).OrderBy(m => m.D).Take(10).Select(m => new PairLine("Improved the most", m.Store, m.A, m.B, true)));
        lines.AddRange(moved.Where(m => m.D > 0).OrderByDescending(m => m.D).Take(10).Select(m => new PairLine("Worsened the most", m.Store, m.A, m.B, true)));
        WriteSectionSheet(wb, "Store Movers", "Direction", "Store", a, b, lines, false, "Turnover rate per store; the 10 stores that moved the most each way.");
    }

    // ── Workforce ─────────────────────────────────────────────────────────────
    private async Task AddWorkforceTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        var uneven = MonthCount(a) != MonthCount(b);

        Task<List<ChartDataItem>> Job(ComparisonSide s) => _dashboard.GetHeadcountByJobTitleAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months, jobTitles: s.Job);
        Task<List<ChartDataItem>> Tenure(ComparisonSide s) => _dashboard.GetHeadcountByTenureAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months, jobTitles: s.Job);
        Task<List<ChartDataItem>> Payroll(ComparisonSide s) => _dashboard.GetHeadcountByPayrollGroupAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months, jobTitles: s.Job);
        Task<List<ChartDataItem>> Gender(ComparisonSide s) => _dashboard.GetGenderBreakdownAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months, jobTitles: s.Job);

        var lines = new List<PairLine>();
        lines.AddRange(PairLines("Job Title", await Job(a), await Job(b), a, b, PairMode.Count));
        lines.AddRange(PairLines("Tenure", await Tenure(a), await Tenure(b), a, b, PairMode.Count, keepOrder: true));
        lines.AddRange(PairLines("Payroll Group", await Payroll(a), await Payroll(b), a, b, PairMode.Count));
        lines.AddRange(PairLines("Gender", await Gender(a), await Gender(b), a, b, PairMode.Count));
        WriteSectionSheet(wb, "Composition", "Section", "Item", a, b, lines, uneven, "Active headcount grouped by the section; " + (UnevenNote(a, b, "counts") ?? "summed over the months of each side."));

        var sa = await LoadMonthlySeriesAsync(a, role, assignedName);
        var sb = await LoadMonthlySeriesAsync(b, role, assignedName);
        (int, double[]) Point(MonthlyPoint p) => (p.Month, new[] { (double)p.Headcount, (double)(p.NewHires - p.Resignations) });
        WriteMonthlySheet(wb, "Headcount Trend", a, b, sa.Select(Point).ToList(), sb.Select(Point).ToList(),
            new[] { ("Headcount", false), ("Net Change (hires − resignations)", false) }, "Each month on its own, for the months of each side.");

        await AddDashboardTeamsSheetAsync(wb, "Teams Headcount", a, b, role, assignedName, workforce: true);
    }

    // ── 90-Day ────────────────────────────────────────────────────────────────
    private async Task AddNinetyTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        var uneven = MonthCount(a) != MonthCount(b);

        async Task<List<(int Month, double[] Values)>> Cohorts(ComparisonSide s)
        {
            var months = (MultiValueFilter.Split(s.Months) ?? new List<string>()).Select(int.Parse).ToHashSet();
            return (await _ninetyDay.GetTrendAsync(s.Store, role, assignedName, s.Om, s.Oc, s.Soc, s.Od))
                .Where(t => t.Year == s.Year && months.Contains(t.Month))
                .Select(t => (t.Month, new[] { t.Rate, (double)t.TotalHires, (double)t.EarlyLeavers })).ToList();
        }
        WriteMonthlySheet(wb, "Cohort Months", a, b, await Cohorts(a), await Cohorts(b),
            new[] { ("90-Day Rate", true), ("Hires", false), ("Early Leavers", false) }, "Each hire-month cohort on its own, for the cohort months of each side.");

        Task<List<ChartDataItem>> Reasons(ComparisonSide s) => _ninetyDay.GetEarlyLeaverReasonsAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months);
        Task<List<ChartDataItem>> Jobs(ComparisonSide s) => _ninetyDay.GetEarlyLeaverJobTitlesAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months);
        Task<List<ChartDataItem>> Payroll(ComparisonSide s) => _ninetyDay.GetEarlyLeaverPayrollGroupsAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months);
        Task<List<ChartDataItem>> Gender(ComparisonSide s) => _ninetyDay.GetEarlyLeaverGenderBreakdownAsync(s.AnchorMonth, s.Year, s.Store, role, assignedName, om: s.Om, oc: s.Oc, soc: s.Soc, od: s.Od, months: s.Months);

        var lines = new List<PairLine>();
        lines.AddRange(PairLines("Reason (share of early leavers)", await Reasons(a), await Reasons(b), a, b, PairMode.Share));
        lines.AddRange(PairLines("Job Title", await Jobs(a), await Jobs(b), a, b, PairMode.Count));
        lines.AddRange(PairLines("Payroll Group", await Payroll(a), await Payroll(b), a, b, PairMode.Count));
        lines.AddRange(PairLines("Gender", await Gender(a), await Gender(b), a, b, PairMode.Count));
        WriteSectionSheet(wb, "Early Leavers Breakdown", "Section", "Item", a, b, lines, uneven,
            "Early leavers (resigned within 90 days of hire); reasons are shares of each side's own answers. " + UnevenNote(a, b, "counts"));

        var oa = await _ninetyDay.GetOcOmAnalysisAsync(a.AnchorMonth, a.Year, role, assignedName, om: a.Om, oc: a.Oc, soc: a.Soc, od: a.Od, months: a.Months);
        var ob = await _ninetyDay.GetOcOmAnalysisAsync(b.AnchorMonth, b.Year, role, assignedName, om: b.Om, oc: b.Oc, soc: b.Soc, od: b.Od, months: b.Months);
        var ws = AddSheet(wb, "Teams 90-Day");
        StyleHeader(ws, new[] { "Level", "Name", "Stores", "Hires A", "Hires B", "Early Leavers A", "Early Leavers B", "90-Day Rate A", "90-Day Rate B", "Delta (A − B)" });
        var levels = new[]
        {
            (TeamLevels[0], oa.OcRows, ob.OcRows), (TeamLevels[1], oa.OmRows, ob.OmRows),
            (TeamLevels[2], oa.SocRows, ob.SocRows), (TeamLevels[3], oa.OdRows, ob.OdRows),
        };
        var row = 2;
        foreach (var (level, rowsA, rowsB) in levels)
        {
            var ma = ByName(rowsA, r => r.Name);
            var mb = ByName(rowsB, r => r.Name);
            var names = ma.Keys.Union(mb.Keys)
                .OrderByDescending(n => (ma.GetValueOrDefault(n)?.AvgRate ?? 0) + (mb.GetValueOrDefault(n)?.AvgRate ?? 0)).ThenBy(n => n).ToList();
            foreach (var name in names)
            {
                ma.TryGetValue(name, out var ra);
                mb.TryGetValue(name, out var rb);
                PutText(ws, row, 1, level);
                PutText(ws, row, 2, name);
                SetIntCell(ws.Cell(row, 3), (ra ?? rb)!.StoreCount);
                PutCount(ws, row, 4, ra == null ? null : PerMonth(a, ra.TotalHires, uneven), uneven);
                PutCount(ws, row, 5, rb == null ? null : PerMonth(b, rb.TotalHires, uneven), uneven);
                PutCount(ws, row, 6, ra == null ? null : PerMonth(a, ra.EarlyLeavers, uneven), uneven);
                PutCount(ws, row, 7, rb == null ? null : PerMonth(b, rb.EarlyLeavers, uneven), uneven);
                double? rateA = ra?.AvgRate, rateB = rb?.AvgRate;
                PutPercent(ws, row, 8, rateA); PutPercent(ws, row, 9, rateB); PutPercent(ws, row, 10, Diff(rateA, rateB));
                row++;
            }
        }
        Finalize(ws);
        AddComparisonNotes(ws, UnevenNote(a, b, "counts"));
    }

    // ── Retention (snapshot at each side's last month) ────────────────────────
    private async Task AddRetentionTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        Task<List<SurvivalPoint>> Curve(ComparisonSide s) => _retention.GetActiveTenureCurveAsync(s.Store, role, assignedName, s.Om, s.Oc, s.Soc, s.Od, s.AnchorMonth, s.Year);
        Task<List<ChartDataItem>> Gender(ComparisonSide s) => _retention.GetGenderRetentionAsync(s.Store, role, assignedName, s.Om, s.Oc, s.Soc, s.Od, s.AnchorMonth, s.Year);
        Task<List<ChartDataItem>> Jobs(ComparisonSide s) => _retention.GetRetentionByJobTitleAsync(s.Store, role, assignedName, s.Om, s.Oc, s.Soc, s.Od, s.AnchorMonth, s.Year);
        static List<ChartDataItem> AsItems(IEnumerable<SurvivalPoint> points) =>
            points.Select(p => new ChartDataItem { Label = p.Label, Value = (int)Math.Round(p.RetentionRate) }).ToList();

        var lines = new List<PairLine>();
        lines.AddRange(PairLines("Team tenure curve (% still here)", AsItems(await Curve(a)), AsItems(await Curve(b)), a, b, PairMode.Percent, keepOrder: true));
        lines.AddRange(PairLines("Retention by gender (6+ months)", await Gender(a), await Gender(b), a, b, PairMode.Percent));
        lines.AddRange(PairLines("Retention by job title (6+ months)", await Jobs(a), await Jobs(b), a, b, PairMode.Percent));
        WriteSectionSheet(wb, "Team Tenure", "Section", "Item", a, b, lines, false,
            "Snapshot of the current team at the last month of each side (percentages, rounded to whole numbers by the source).");

        var rankA = (await _retention.GetStoreRetentionRankingAsync(a.Store, role, assignedName, a.Om, a.Oc, a.Soc, a.Od, a.AnchorMonth, a.Year)).ToDictionary(r => r.Label, r => (double)r.Value);
        var rankB = (await _retention.GetStoreRetentionRankingAsync(b.Store, role, assignedName, b.Om, b.Oc, b.Soc, b.Od, b.AnchorMonth, b.Year)).ToDictionary(r => r.Label, r => (double)r.Value);
        var tenA = (await _retention.GetAverageTenureByStoreAsync(a.Store, role, assignedName, a.Om, a.Oc, a.Soc, a.Od, a.AnchorMonth, a.Year)).ToDictionary(r => r.Label, r => (double)r.Value);
        var tenB = (await _retention.GetAverageTenureByStoreAsync(b.Store, role, assignedName, b.Om, b.Oc, b.Soc, b.Od, b.AnchorMonth, b.Year)).ToDictionary(r => r.Label, r => (double)r.Value);
        var stores = rankA.Keys.Where(rankB.ContainsKey).OrderByDescending(s => Math.Abs(rankA[s] - rankB[s])).ThenBy(s => s).ToList();

        var ws = AddSheet(wb, "Stores Retention");
        StyleHeader(ws, new[] { "Store", "Retention 6+ Months A", "Retention 6+ Months B", "Delta (A − B)", "Avg Tenure (months) A", "Avg Tenure (months) B", "Delta (A − B)" });
        for (var i = 0; i < stores.Count; i++)
        {
            var row = i + 2;
            PutText(ws, row, 1, stores[i]);
            PutPercent(ws, row, 2, rankA[stores[i]]); PutPercent(ws, row, 3, rankB[stores[i]]); PutPercent(ws, row, 4, rankA[stores[i]] - rankB[stores[i]]);
            double? ta = tenA.TryGetValue(stores[i], out var x) ? x : null;
            double? tb = tenB.TryGetValue(stores[i], out var y) ? y : null;
            PutCount(ws, row, 5, ta, false); PutCount(ws, row, 6, tb, false); PutCount(ws, row, 7, Diff(ta, tb), false);
        }
        Finalize(ws);
        AddComparisonNotes(ws, "Snapshot at the last month of each side; stores that appear on both sides, those that moved the most first.");

        var wt = AddSheet(wb, "Teams Tenure");
        StyleHeader(wt, new[] { "Level", "Name", "Stores", "Headcount A", "Headcount B", "Avg Tenure (months) A", "Avg Tenure (months) B", "Delta (A − B)" });
        var row2 = 2;
        foreach (var (dimension, level) in new[] { ("oc", TeamLevels[0]), ("om", TeamLevels[1]), ("soc", TeamLevels[2]), ("od", TeamLevels[3]) })
        {
            var ma = ByName(await _retention.GetAverageTenureByManagerAsync(dimension, role, assignedName, a.AnchorMonth, a.Year, a.Store, a.Om, a.Oc, a.Soc, a.Od), r => r.Name);
            var mb = ByName(await _retention.GetAverageTenureByManagerAsync(dimension, role, assignedName, b.AnchorMonth, b.Year, b.Store, b.Om, b.Oc, b.Soc, b.Od), r => r.Name);
            var names = ma.Keys.Union(mb.Keys)
                .OrderByDescending(n => (ma.GetValueOrDefault(n)?.AvgTenureMonths ?? 0) + (mb.GetValueOrDefault(n)?.AvgTenureMonths ?? 0)).ThenBy(n => n).ToList();
            foreach (var name in names)
            {
                ma.TryGetValue(name, out var ra);
                mb.TryGetValue(name, out var rb);
                PutText(wt, row2, 1, level);
                PutText(wt, row2, 2, name);
                SetIntCell(wt.Cell(row2, 3), (ra ?? rb)!.Stores);
                PutCount(wt, row2, 4, ra?.Headcount, false); PutCount(wt, row2, 5, rb?.Headcount, false);
                double? tenureA = ra?.AvgTenureMonths, tenureB = rb?.AvgTenureMonths;
                PutCount(wt, row2, 6, tenureA, false); PutCount(wt, row2, 7, tenureB, false); PutCount(wt, row2, 8, Diff(tenureA, tenureB), false);
                row2++;
            }
        }
        Finalize(wt);
        AddComparisonNotes(wt, "Snapshot at the last month of each side.");
    }

    // ── Exit Interviews (answers as shares of each side's own answers) ───────
    private async Task AddExitTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        var fa = ExitFilterOf(a);
        var fb = ExitFilterOf(b);
        var lines = new List<PairLine>();
        lines.AddRange(PairLines("Would they return?", await _exitInterviews.GetWouldReturnAsync(fa, role, assignedName), await _exitInterviews.GetWouldReturnAsync(fb, role, assignedName), a, b, PairMode.Share));
        lines.AddRange(PairLines("Overall experience", await _exitInterviews.GetOverallExperienceAsync(fa, role, assignedName), await _exitInterviews.GetOverallExperienceAsync(fb, role, assignedName), a, b, PairMode.Share));
        lines.AddRange(PairLines("Received adequate training?", await _exitInterviews.GetTrainingAsync(fa, role, assignedName), await _exitInterviews.GetTrainingAsync(fb, role, assignedName), a, b, PairMode.Share));
        lines.AddRange(PairLines("Fair treatment", await _exitInterviews.GetFairTreatmentAsync(fa, role, assignedName), await _exitInterviews.GetFairTreatmentAsync(fb, role, assignedName), a, b, PairMode.Share));
        lines.AddRange(PairLines("Workload conditions", await _exitInterviews.GetWorkloadConditionAsync(fa, role, assignedName), await _exitInterviews.GetWorkloadConditionAsync(fb, role, assignedName), a, b, PairMode.Share));
        lines.AddRange(PairLines("Reason for workload pressure", await _exitInterviews.GetWorkPressureReasonAsync(fa, role, assignedName), await _exitInterviews.GetWorkPressureReasonAsync(fb, role, assignedName), a, b, PairMode.Share));
        WriteSectionSheet(wb, "Exit Experience", "Question", "Answer", a, b, lines, false,
            "Each answer as a percentage of that side's own answers to the question, so sides with a different number of interviews stay comparable.");

        var more = new List<PairLine>();
        more.AddRange(PairLines("Exit interviews by job title (share)", await _exitInterviews.GetByJobTitleAsync(fa, role, assignedName), await _exitInterviews.GetByJobTitleAsync(fb, role, assignedName), a, b, PairMode.Share));
        var ra = (await _exitInterviews.GetReasonVsWouldReturnAsync(fa, role, assignedName)).ToDictionary(r => BlankAs(r.Reason), r => r.WouldReturnPercent);
        var rb = (await _exitInterviews.GetReasonVsWouldReturnAsync(fb, role, assignedName)).ToDictionary(r => BlankAs(r.Reason), r => r.WouldReturnPercent);
        more.AddRange(ra.Keys.Union(rb.Keys).OrderBy(k => k).Select(k => new PairLine("Would return, by reason for leaving", k,
            ra.TryGetValue(k, out var x) ? x : null, rb.TryGetValue(k, out var y) ? y : null, true)));
        WriteSectionSheet(wb, "Exit By Job and Return", "Section", "Item", a, b, more, false,
            "Reasons shown only when at least two people gave them (as on the Exit Interviews page).");
    }

    // ── Scorecard ─────────────────────────────────────────────────────────────
    private async Task AddScorecardTabsAsync(XLWorkbook wb, ComparisonSide a, ComparisonSide b, string role, string? assignedName)
    {
        Task<List<ScorecardRow>> Load(string dimension, ComparisonSide s) => _scorecard.GetScorecardAsync(dimension, role, assignedName, s.Om, s.Oc, s.Soc, s.Od, s.Months, s.Year);
        string[] headers(string first) => new[]
        {
            "Level", first, "Turnover Rate A", "Turnover Rate B", "Delta (A − B)", "90-Day Early Leave A", "90-Day Early Leave B", "Delta (A − B)",
            "180-Day Retention A", "180-Day Retention B", "Delta (A − B)", "Exit Sentiment A", "Exit Sentiment B", "Delta (A − B)",
        };
        void WriteMeasures(IXLWorksheet ws, int row, ScorecardRow? ra, ScorecardRow? rb)
        {
            var pairs = new (double? A, double? B)[]
            {
                (ra?.TurnoverRate, rb?.TurnoverRate), (ra?.EarlyLeaver90Rate, rb?.EarlyLeaver90Rate),
                (ra?.Retention180Rate, rb?.Retention180Rate), (ra?.ExitSentimentPercent, rb?.ExitSentimentPercent),
            };
            for (var k = 0; k < pairs.Length; k++)
            {
                PutPercent(ws, row, 3 + k * 3, pairs[k].A); PutPercent(ws, row, 4 + k * 3, pairs[k].B); PutPercent(ws, row, 5 + k * 3, Diff(pairs[k].A, pairs[k].B));
            }
        }

        var wsSup = AddSheet(wb, "Supervisors Scorecard");
        StyleHeader(wsSup, headers("Name"));
        var row = 2;
        foreach (var (dimension, level) in new[] { ("oc", TeamLevels[0]), ("om", TeamLevels[1]) })
        {
            var ma = ByName(await Load(dimension, a), r => r.Name);
            var mb = ByName(await Load(dimension, b), r => r.Name);
            var names = ma.Keys.Union(mb.Keys)
                .OrderByDescending(n => (ma.GetValueOrDefault(n)?.TurnoverRate ?? 0) + (mb.GetValueOrDefault(n)?.TurnoverRate ?? 0)).ThenBy(n => n).ToList();
            foreach (var name in names)
            {
                ma.TryGetValue(name, out var ra);
                mb.TryGetValue(name, out var rb);
                PutText(wsSup, row, 1, level);
                PutText(wsSup, row, 2, name);
                WriteMeasures(wsSup, row, ra, rb);
                row++;
            }
        }
        Finalize(wsSup);
        AddComparisonNotes(wsSup, "The four Scorecard measures per supervisor. Turnover and early leave: lower is better; retention and exit sentiment: higher is better. The Store and Job Title filters do not apply.");

        var leadersA = ByName(await Load("leader", a), r => r.Name);
        var leadersB = ByName(await Load("leader", b), r => r.Name);
        var wsLead = AddSheet(wb, "Leaders Scorecard");
        StyleHeader(wsLead, headers("Store Leader"));
        row = 2;
        foreach (var name in leadersA.Keys.Where(leadersB.ContainsKey).OrderBy(n => leadersA[n].TurnoverRate - leadersB[n].TurnoverRate).ThenBy(n => n))
        {
            PutText(wsLead, row, 1, "Store Leader");
            PutText(wsLead, row, 2, name);
            WriteMeasures(wsLead, row, leadersA[name], leadersB[name]);
            row++;
        }
        Finalize(wsLead);
        AddComparisonNotes(wsLead, "Leaders present on both sides; those whose turnover improved the most first.");
    }
}
