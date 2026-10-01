using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>Stops a User-interface page that an Admin hid (Settings → Pages) from opening: the user is sent to the
/// first page that is still visible, with a short message. Only page views are covered (see
/// <see cref="UserPages.KeyOfAction"/>); downloads and the APIs the pages call are not affected.</summary>
public class UserPageVisibilityFilter : IAsyncActionFilter
{
    private readonly IPageVisibilityService _visibility;
    private readonly IStringLocalizer<SharedResource> _L;

    public UserPageVisibilityFilter(IPageVisibilityService visibility, IStringLocalizer<SharedResource> localizer)
    {
        _visibility = visibility;
        _L = localizer;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = UserPages.KeyOfAction(context.RouteData.Values["action"]?.ToString());
        if (key != null)
        {
            var hidden = await _visibility.GetHiddenAsync();
            if (hidden.Contains(key) && UserPages.FirstVisible(hidden) is { } target)
            {
                if (context.Controller is Controller c) c.TempData["Error"] = _L["Msg_PageHidden"].Value;
                context.Result = new RedirectResult("/home/dashboard/" + target.Action.ToLowerInvariant());
                return;
            }
        }
        await next();
    }
}
