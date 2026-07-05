using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;

namespace MvcApp.Areas.Home.Controllers;

[Area("Home")]
[RequireUserAuth]
public class DashboardController : Controller
{
    public IActionResult Index() => Redirect("/dashboard");
}
