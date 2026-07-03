using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;

namespace MvcApp.Controllers;

[RequireAuth]
public class DashboardController : Controller
{
    public IActionResult Index()
    {
        var role = HttpContext.Session.GetRole();
        if (role == "Admin_Full" || role == "Admin_Read")
            return RedirectToAction("Turnover", "Admin");

        return View();
    }
}
