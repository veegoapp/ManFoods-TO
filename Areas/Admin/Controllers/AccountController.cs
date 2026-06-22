using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    public AccountController(IAuthService auth) => _auth = auth;

    [HttpGet]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetUserId() != null && HttpContext.Session.IsAdmin())
            return RedirectToAction("Analytics", "Dashboard", new { area = "Admin" });
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null) { ModelState.AddModelError("", "Invalid email or password"); return View(vm); }

        if (user.Role != "Admin_Full" && user.Role != "Admin_Read")
        {
            ModelState.AddModelError("", "Access denied. Admin credentials required.");
            return View(vm);
        }

        HttpContext.Session.SetUserSession(user.Id, user.Email, user.Role, user.AssignedName);
        return RedirectToAction("Analytics", "Dashboard", new { area = "Admin" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    [RequireAdminAuth]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [RequireAdminAuth]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return RedirectToAction("Login");
        var ok = await _auth.ChangePasswordAsync(userId.Value, vm.CurrentPassword, vm.NewPassword);
        if (!ok) { ModelState.AddModelError("CurrentPassword", "Current password is incorrect"); return View(vm); }
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("ChangePassword");
    }
}
