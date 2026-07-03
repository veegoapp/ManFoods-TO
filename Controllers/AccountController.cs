using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;

namespace MvcApp.Controllers;

public class AccountController : Controller
{
    public IActionResult Login()
    {
        if (HttpContext.Session.GetUserId() != null)
        {
            return HttpContext.Session.IsAdmin()
                ? Redirect("/admin/dashboard/turnover")
                : Redirect("/home/dashboard");
        }
        return Redirect("/home/account/login");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Redirect("/home/account/login");
    }
}
