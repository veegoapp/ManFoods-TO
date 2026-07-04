using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MvcApp.Filters;

public class RequireAdminAuthAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetInt32("UserId");
        if (userId == null)
        {
            context.Result = new RedirectResult("/adminlogin");
            return;
        }
        var role = session.GetString("Role") ?? "";
        if (role != "Admin")
        {
            context.Result = new RedirectResult("/adminlogin");
        }
    }
}
