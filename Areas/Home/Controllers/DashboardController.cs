using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Areas.Home.Controllers;

[Area("Home")]
[RequireUserAuth]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly IStoreService _stores;
    private readonly IReportService _reports;

    public DashboardController(IDashboardService dashboard, IStoreService stores, IReportService reports)
    {
        _dashboard = dashboard;
        _stores = stores;
        _reports = reports;
    }

    public IActionResult Index() => RedirectToAction("Workforce");

    public IActionResult Turnover() => View();

    public IActionResult Comparisons() => View();

    public IActionResult Workforce() => View();

    public IActionResult Retention() => View();

    public IActionResult Stores() => View();

    public IActionResult ExitInterviews() => View();

    public IActionResult NinetyDayTurnover() => View();

    public IActionResult EarlyWarning() => View();

    public IActionResult Scorecard() => View();

    public IActionResult StoreLeaderProfile() => View();

    public IActionResult StoreProfile() => View();

    public IActionResult ActionCenter() => View();

    public IActionResult ActionCenterDetail() => View();

    public IActionResult ActionPlanGuide() => View();

    public async Task<IActionResult> Reports()
    {
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        return View(periods);
    }

    [HttpGet("home/dashboard/reports/{reportType}")]
    [AccessArea(AccessAreas.Shared)]
    public async Task<IActionResult> ReportDetail(string reportType)
    {
        if (MvcApp.Models.ViewModels.ReportCatalog.Find(reportType) == null) return NotFound();

        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        var stores = await _stores.GetStoresAsync(null, null, role, assignedName);
        ViewBag.Stores = stores.Select(s => s.StoreName).Distinct().OrderBy(s => s).ToList();
        ViewBag.OperationManagers = await _dashboard.GetOperationManagersAsync(null, null, role, assignedName);
        ViewBag.OperationConsultants = await _dashboard.GetOperationConsultantsAsync(null, null, role, assignedName);
        ViewBag.SeniorOperationConsultants = await _dashboard.GetSeniorOperationConsultantsAsync(null, null, role, assignedName);
        ViewBag.OperationDirectors = await _dashboard.GetOperationDirectorsAsync(null, null, role, assignedName);
        ViewBag.ReportType = reportType;
        return View(periods);
    }

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private async Task<IActionResult> DownloadWorkbookAsync(XLWorkbook wb, string fileName)
    {
        using (wb)
        {
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), XlsxContentType, fileName);
        }
    }

    // Mirrors Areas/Admin/Controllers/DashboardController.Export exactly (same IReportService
    // calls) so the shared Reports view's download buttons work under the Home area too.
    [HttpGet("home/dashboard/export")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [AccessArea(AccessAreas.Reports)]
    public async Task<IActionResult> Export(int month, int year, string reportType = "stores-overview",
        string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null,
        int? yearB = null, string? monthsB = null, string? storeB = null, string? omB = null, string? ocB = null, string? socB = null, string? odB = null)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetEmail();
        store = string.IsNullOrWhiteSpace(store) ? null : store;
        om = string.IsNullOrWhiteSpace(om) ? null : om;
        oc = string.IsNullOrWhiteSpace(oc) ? null : oc;
        soc = string.IsNullOrWhiteSpace(soc) ? null : soc;
        od = string.IsNullOrWhiteSpace(od) ? null : od;
        months = string.IsNullOrWhiteSpace(months) ? null : months;
        storeB = string.IsNullOrWhiteSpace(storeB) ? null : storeB;
        omB = string.IsNullOrWhiteSpace(omB) ? null : omB;
        ocB = string.IsNullOrWhiteSpace(ocB) ? null : ocB;
        socB = string.IsNullOrWhiteSpace(socB) ? null : socB;
        odB = string.IsNullOrWhiteSpace(odB) ? null : odB;
        monthsB = string.IsNullOrWhiteSpace(monthsB) ? null : monthsB;

        switch (reportType)
        {
            case "comparisons":
                return await DownloadWorkbookAsync(
                    await _reports.BuildComparisonReportAsync(role, assignedName,
                        year > 0 ? year : null, months, store, om, oc, soc, od,
                        yearB, monthsB, storeB, omB, ocB, socB, odB),
                    "Comparison_Report.xlsx");
            case "stores":
                return await DownloadWorkbookAsync(
                    await _reports.BuildStoreComparisonReportAsync(month, year, role, assignedName, om, oc, soc, od),
                    $"Store_Comparison_{year}_{month:D2}.xlsx");
            case "turnover":
                return await DownloadWorkbookAsync(await _reports.BuildTurnoverReportAsync(role, assignedName, store), "Turnover_Report.xlsx");
            case "ninety-day":
                return await DownloadWorkbookAsync(await _reports.BuildNinetyDayReportAsync(role, assignedName, store), "90_Day_Turnover_Report.xlsx");
            case "retention":
                return await DownloadWorkbookAsync(await _reports.BuildRetentionReportAsync(role, assignedName, store), "Retention_Report.xlsx");
            case "exit-interviews":
                return await DownloadWorkbookAsync(await _reports.BuildExitInterviewReportAsync(role, assignedName, store, om, oc), "Exit_Interview_Report.xlsx");
            case "scorecard":
                return await DownloadWorkbookAsync(await _reports.BuildScorecardReportAsync(role, assignedName, om, oc, soc, od, months, year > 0 ? year : null), "Scorecard_Report.xlsx");
            case "early-warning":
                return await DownloadWorkbookAsync(
                    await _reports.BuildEarlyWarningReportAsync(role, assignedName, store, om, oc, soc, od, months, year > 0 ? year : null),
                    "Early_Warning_Report.xlsx");
            case "trend-matrix":
                return await DownloadWorkbookAsync(
                    await _reports.BuildTrendMatrixReportAsync(role, assignedName, om, oc, soc, od, year > 0 ? year : null, months),
                    $"Turnover_Trend_Matrix_{year}.xlsx");
            case "ninety-day-trend-matrix":
                return await DownloadWorkbookAsync(
                    await _reports.BuildNinetyDayTrendMatrixReportAsync(role, assignedName, om, oc, soc, od, months, year > 0 ? year : null),
                    "90_Day_Trend_Matrix_Report.xlsx");
            case "action-center":
                return await DownloadWorkbookAsync(await _reports.BuildActionCenterReportAsync(role, assignedName, om, oc, soc, od), "Action_Center_Report.xlsx");
            case "stores-overview":
                return await DownloadWorkbookAsync(
                    await _reports.BuildStoresOverviewReportAsync(month, year, role, assignedName, om, oc, soc, od),
                    $"Stores_Overview_{year}_{month:D2}.xlsx");
            case "workforce":
                return await DownloadWorkbookAsync(
                    await _reports.BuildWorkforceReportAsync(month, year, role, assignedName, store, om, oc, soc, od),
                    $"Workforce_Report_{year}_{month:D2}.xlsx");
            case "oc-om-comparison":
                return await DownloadWorkbookAsync(
                    await _reports.BuildOcOmComparisonReportAsync(month, year, role, assignedName, om: om, oc: oc, soc: soc, od: od),
                    $"OC_OM_Comparison_{year}_{month:D2}.xlsx");
            default:
                return NotFound();
        }
    }
}
