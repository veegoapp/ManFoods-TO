using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Filters;

public class RequireAuthAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/login");
}
