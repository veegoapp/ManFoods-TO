using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MvcApp.Filters;

public class RequireRoleAttribute : ActionFilterAttribute
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var userId = context.HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }
        var role = context.HttpContext.Session.GetString("Role") ?? "";
        if (!_roles.Contains(role))
        {
            context.Result = new ForbidResult();
        }
    }
}
