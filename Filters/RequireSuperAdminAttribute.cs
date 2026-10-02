using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Services;

namespace MvcApp.Filters;

/// <summary>Only the Super Admin account (see <see cref="SuperAdminPolicy"/>) — never an ordinary Admin. The check
/// reads the server-side session after it has been re-validated against the database; nothing comes from the client.
/// Refusal is a plain 403 and is recorded in the Activity Logs.</summary>
public class RequireSuperAdminAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/adminlogin");

    protected override IActionResult? OnRoleCheck(string role) =>
        role == "Admin" ? null : new StatusCodeResult(StatusCodes.Status403Forbidden);

    protected override IActionResult? OnSessionCheck(ISession session) =>
        SuperAdminPolicy.IsSuperAdmin(session.GetEmail()) ? null : new StatusCodeResult(StatusCodes.Status403Forbidden);
}
