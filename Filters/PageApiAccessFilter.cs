using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MvcApp.Extensions;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>
/// Global filter enforcing <see cref="RequiresAnyPageAttribute"/>: the APIs and downloads behind a page are
/// closed to a role the page is hidden from. Runs after the authentication filters (Order 100), so a signed-out
/// caller still gets the login redirect. Admin, and the shared filter-dropdown lookups
/// (<see cref="AccessAreas.Shared"/>), are exempt. This only adds a refusal; it never widens anything, and it
/// does not touch store scoping or the per-area Access settings.
/// </summary>
public sealed class PageApiAccessFilter : IAsyncActionFilter
{
    private readonly IPageVisibilityService _visibility;

    public PageApiAccessFilter(IPageVisibilityService visibility) => _visibility = visibility;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var requirement = metadata.OfType<RequiresAnyPageAttribute>().LastOrDefault();
        var isSharedLookup = metadata.OfType<AccessAreaAttribute>().LastOrDefault()?.Area == AccessAreas.Shared;
        if (requirement == null || requirement.PageKeys.Count == 0 || isSharedLookup)
        {
            await next();
            return;
        }

        var session = context.HttpContext.Session;
        var role = session.GetRole();
        if (session.GetUserId() == null || role == "Admin")
        {
            await next();
            return;
        }

        var hidden = await _visibility.GetHiddenAsync(role);
        if (requirement.PageKeys.Any(key => !hidden.Contains(key)))
        {
            await next();
            return;
        }

        context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
