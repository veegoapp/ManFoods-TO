using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Filters;

public class RequireUserAuthAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/login");

    protected override IActionResult? OnRoleCheck(string role) => role == "Admin" ? new RedirectResult("/login") : null;
}
