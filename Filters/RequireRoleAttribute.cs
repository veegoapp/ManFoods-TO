using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Filters;

public class RequireRoleAttribute : SessionAuthFilterAttribute
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = roles;
    }

    protected override IActionResult OnUnauthenticated() => new RedirectToActionResult("Login", "Account", null);

    protected override IActionResult? OnRoleCheck(string role) => _roles.Contains(role) ? null : new ForbidResult();
}
