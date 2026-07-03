using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MvcApp.Filters;

public class RequireUserAuthAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetInt32("UserId");
        if (userId == null)
        {
            context.Result = new RedirectToActionResult("Login", "Account", new { area = "Home" });
            return;
        }
        var role = session.GetString("Role") ?? "";
        if (role == "Admin")
        {
            context.Result = new RedirectToActionResult("Login", "Account", new { area = "Home" });
        }
    }
}
