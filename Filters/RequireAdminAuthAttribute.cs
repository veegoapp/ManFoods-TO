using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Filters;

public class RequireAdminAuthAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/adminlogin");

    protected override IActionResult? OnRoleCheck(string role) => role == "Admin" ? null : new RedirectResult("/adminlogin");
}
