using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;

namespace MvcApp.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (HttpContext.Session.GetUserId() == null)
            return Redirect("/login");

        return HttpContext.Session.IsAdmin()
            ? Redirect("/admin/dashboard/turnover")
            : Redirect("/home/dashboard");
    }

    public IActionResult Error() =>
        View(new MvcApp.Models.ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
