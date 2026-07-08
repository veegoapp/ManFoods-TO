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

    public IActionResult Index() => RedirectToAction("Turnover");

    public IActionResult Turnover() => View();

    public IActionResult Comparisons() => View();

    public IActionResult Workforce() => View();

    public IActionResult Retention() => View();

    public IActionResult Stores() => View();

    public IActionResult ExitInterviews() => View();

    public IActionResult NinetyDayTurnover() => View();

    public IActionResult AiAssistant() => View();

    public IActionResult EarlyWarning() => View();

    public IActionResult Scorecard() => View();

    public async Task<IActionResult> Reports()
    {
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        return View(periods);
    }

    [HttpGet("home/dashboard/reports/{reportType}")]
    public async Task<IActionResult> ReportDetail(string reportType)
    {
        if (MvcApp.Models.ViewModels.ReportCatalog.Find(reportType) == null) return NotFound();

        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        var stores = await _stores.GetStoresAsync(null, null, role, assignedName);
        ViewBag.Stores = stores.Select(s => s.StoreName).Distinct().OrderBy(s => s).ToList();
        ViewBag.OperationManagers = await _dashboard.GetOperationManagersAsync(null, null);
        ViewBag.OperationConsultants = await _dashboard.GetOperationConsultantsAsync(null, null);
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
    public async Task<IActionResult> Export(int month, int year, string reportType = "summary",
        string? store = null, string? om = null, string? oc = null)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        store = string.IsNullOrWhiteSpace(store) ? null : store;
        om = string.IsNullOrWhiteSpace(om) ? null : om;
        oc = string.IsNullOrWhiteSpace(oc) ? null : oc;

        switch (reportType)
        {
            case "stores":
                return await DownloadWorkbookAsync(
                    await _reports.BuildStoreComparisonReportAsync(month, year, role, assignedName, om, oc),
                    $"Store_Comparison_{year}_{month:D2}.xlsx");
            case "ninety-day":
                return await DownloadWorkbookAsync(await _reports.BuildNinetyDayReportAsync(store), "90_Day_Turnover_Report.xlsx");
            case "retention":
                return await DownloadWorkbookAsync(await _reports.BuildRetentionReportAsync(store), "Retention_Report.xlsx");
            case "exit-interviews":
                return await DownloadWorkbookAsync(await _reports.BuildExitInterviewReportAsync(store, om, oc), "Exit_Interview_Report.xlsx");
            case "scorecard":
                return await DownloadWorkbookAsync(await _reports.BuildScorecardReportAsync(om, oc), "Scorecard_Report.xlsx");
            case "early-warning":
                return await DownloadWorkbookAsync(await _reports.BuildEarlyWarningReportAsync(store), "Early_Warning_Report.xlsx");
            case "trend-matrix":
                return await DownloadWorkbookAsync(
                    await _reports.BuildTrendMatrixReportAsync(role, assignedName, om, oc, year > 0 ? year : null),
                    $"Turnover_Trend_Matrix_{year}.xlsx");
            case "full":
                return await DownloadWorkbookAsync(
                    await _reports.BuildFullReportAsync(month, year, role, assignedName, store),
                    $"Full_Company_Report_{year}_{month:D2}.xlsx");
            default:
                return await DownloadWorkbookAsync(
                    await _reports.BuildSummaryReportAsync(month, year, role, assignedName, store),
                    $"Summary_Report_{year}_{month:D2}.xlsx");
        }
    }
}
