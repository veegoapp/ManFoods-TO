using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Extensions;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>
/// Shared base for every session-based auth filter. Confirms the session has a
/// UserId, then re-validates its cached Role against the database via
/// ISessionValidationService — so a deleted or role-changed user's session stops
/// working well before its natural expiry, instead of every filter re-trusting
/// whatever was cached at login. On failure the session is cleared and treated
/// exactly like "never logged in" (same redirect subclasses already used for
/// that case), so a stale session can't be distinguished from no session at all.
/// </summary>
public abstract class SessionAuthFilterAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetUserId();
        if (userId == null)
        {
            context.Result = OnUnauthenticated();
            return;
        }

        var role = session.GetRole();
        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidationService>();
        if (!await validator.IsValidAsync(userId.Value, role))
        {
            session.Clear();
            context.Result = OnUnauthenticated();
            return;
        }

        var denied = OnRoleCheck(role);
        if (denied != null)
        {
            context.Result = denied;
            return;
        }

        await next();
    }

    /// <summary>Result to return when there's no session, or the session no longer matches the database.</summary>
    protected abstract IActionResult OnUnauthenticated();

    /// <summary>Optional extra role check, run only once the session is confirmed to still match the database. Return null to allow.</summary>
    protected virtual IActionResult? OnRoleCheck(string role) => null;
}
