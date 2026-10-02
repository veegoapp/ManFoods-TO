using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Extensions;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>
/// Shared base for every session-based auth filter. Confirms the session has a
/// UserId (and is younger than the absolute session lifetime), then re-validates its
/// cached Role and password stamp against the database via
/// ISessionValidationService — so a deleted, role-changed or password-changed user's session stops
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

        // Absolute maximum age, independent of activity (the idle timeout alone lets a
        // continuously-used cookie live forever). Treated exactly like "never logged in".
        if (session.IsPastAbsoluteLifetime(DateTimeOffset.UtcNow))
        {
            session.Clear();
            context.Result = OnUnauthenticated();
            return;
        }

        var role = session.GetRole();
        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidationService>();
        if (!await validator.IsValidAsync(userId.Value, role, session.GetPasswordFingerprint()))
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

        var mustChangePassword = OnMustChangePasswordCheck(context, session);
        if (mustChangePassword != null)
        {
            context.Result = mustChangePassword;
            return;
        }

        await next();
    }

    /// <summary>Result to return when there's no session, or the session no longer matches the database.</summary>
    protected abstract IActionResult OnUnauthenticated();

    /// <summary>Optional extra role check, run only once the session is confirmed to still match the database. Return null to allow.</summary>
    protected virtual IActionResult? OnRoleCheck(string role) => null;

    /// <summary>Optional forced-redirect check for an account still on a
    /// system-generated temporary password (session.GetMustChangePassword()) —
    /// overridden only by the page-level filters (RequireUserAuth/
    /// RequireAdminAuth), never by the API filters (RequireAuth/RequireRole),
    /// since redirecting an API/JSON call would just break it. Must let the
    /// Change Password action itself through to avoid a redirect loop.
    /// Return null to allow.</summary>
    protected virtual IActionResult? OnMustChangePasswordCheck(ActionExecutingContext context, ISession session) => null;
}
