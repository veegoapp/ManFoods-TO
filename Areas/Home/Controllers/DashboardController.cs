using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;

namespace MvcApp.Areas.Home.Controllers;

[Area("Home")]
[RequireUserAuth]
public class DashboardController : Controller
{
    public IActionResult Index()
        => RedirectToAction("Turnover");

    public IActionResult Turnover()
        => View("~/Areas/Admin/Views/Dashboard/Turnover.cshtml");

    public IActionResult Comparisons()
        => View("~/Areas/Admin/Views/Dashboard/Comparisons.cshtml");

    public IActionResult Workforce()
        => View("~/Areas/Admin/Views/Dashboard/Workforce.cshtml");

    public IActionResult Retention()
        => View("~/Areas/Admin/Views/Dashboard/Retention.cshtml");

    public IActionResult Stores()
        => View("~/Areas/Admin/Views/Dashboard/Stores.cshtml");

    public IActionResult ExitInterviews()
        => View("~/Areas/Admin/Views/Dashboard/ExitInterviews.cshtml");

    public IActionResult NinetyDayTurnover()
        => View("~/Areas/Admin/Views/Dashboard/NinetyDayTurnover.cshtml");

    public IActionResult AiAssistant()
        => View("~/Areas/Admin/Views/Dashboard/AiAssistant.cshtml");

    public IActionResult EarlyWarning()
        => View("~/Areas/Admin/Views/Dashboard/EarlyWarning.cshtml");

    public IActionResult Scorecard()
        => View("~/Areas/Admin/Views/Dashboard/Scorecard.cshtml");

    public IActionResult Reports()
        => View("~/Areas/Admin/Views/Dashboard/Reports.cshtml");
}