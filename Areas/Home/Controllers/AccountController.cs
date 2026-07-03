using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Areas.Home.Controllers;

[Area("Home")]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly IOtpService _otp;
    public AccountController(IAuthService auth, IOtpService otp) { _auth = auth; _otp = otp; }

    [HttpGet]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetUserId() != null && !HttpContext.Session.IsAdmin())
            return RedirectToAction("Index", "Dashboard", new { area = "Home" });
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null) { ModelState.AddModelError("", "Invalid email or password"); return View(vm); }

        if (user.Role == "Admin")
        {
            ModelState.AddModelError("", "Admin accounts must use the admin portal.");
            return View(vm);
        }

        HttpContext.Session.SetUserSession(user.Id, user.Email, user.Role, user.AssignedName);
        return RedirectToAction("Index", "Dashboard", new { area = "Home" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var (success, message) = await _otp.VerifyAndResetPasswordAsync(vm.Identifier, vm.OtpCode, vm.NewPassword);
        if (!success) { ModelState.AddModelError("", message); return View(vm); }

        TempData["Success"] = message;
        return RedirectToAction("Login");
    }

    [HttpGet]
    [RequireUserAuth]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [RequireUserAuth]
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
