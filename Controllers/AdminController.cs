using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Controllers;

public class AdminController : Controller
{
    public IActionResult Index()   => Redirect("/admin/dashboard/turnover");
    public IActionResult Turnover()  => Redirect("/admin/dashboard/turnover");
    public IActionResult Uploads()   => Redirect("/admin/dashboard/uploads");
    public IActionResult Reports()   => Redirect("/admin/dashboard/reports");
    public IActionResult Users()     => Redirect("/admin/dashboard/users");
}
