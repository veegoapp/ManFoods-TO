using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Areas.Home.Controllers;

[Area("Home")]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly IOtpService _otp;
    private readonly IMemoryCache _cache;
    private readonly IStringLocalizer<SharedResource> _L;
    public AccountController(IAuthService auth, IOtpService otp, IMemoryCache cache, IStringLocalizer<SharedResource> localizer) { _auth = auth; _otp = otp; _cache = cache; _L = localizer; }

    [HttpGet("/login")]
    public IActionResult Login(string? setupToken)
    {
        if (HttpContext.Session.GetUserId() != null && !HttpContext.Session.IsAdmin())
            return RedirectToAction("Index", "Dashboard", new { area = "Home" });
        // Set by the redirect below right after a temporary-password login —
        // the view opens the Update Password popup automatically when present.
        ViewData["SetupToken"] = setupToken;
        return View(new LoginViewModel());
    }

    [HttpPost("/login"), ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var (user, _, isLockedOut, isTempPasswordExpired) = await _auth.ValidateAsync(vm.Email, vm.Password, "Home");
        if (user == null)
        {
            ModelState.AddModelError("", isLockedOut ? _L["Msg_AccountLockedOut"]
                : isTempPasswordExpired ? _L["Msg_TempPasswordExpired"]
                : _L["Msg_InvalidCredentials"]);
            return View(vm);
        }

        if (user.Role == "Admin")
        {
            ModelState.AddModelError("", _L["Msg_AdminUseAdminPortal"]);
            return View(vm);
        }

        // A system-generated temporary password: never start an authenticated
        // session on it. Instead, redirect back to this same login page with a
        // one-time setup token that opens the Update Password popup — see
        // SetupPassword below. They sign in fresh once it's set.
        if (user.MustChangePassword)
            return RedirectToAction("Login", new { setupToken = _cache.BeginPasswordSetup(user) });

        // Session-fixation mitigation: hand the authenticated identity off
        // via a one-time token rather than writing it into whatever session
        // this request arrived with — see SessionExtensions.BeginSessionRotation.
        var token = HttpContext.BeginSessionRotation(_cache, user.Id, user.Email, user.Role, user.AssignedName, user.MustChangePassword);
        return RedirectToAction("CompleteLogin", new { token });
    }

    // Called via fetch from the Update Password popup on the login page —
    // not a page navigation, so this returns JSON rather than a View. Setting
    // the password here never establishes a session (see BeginPasswordSetup):
    // on success the popup just closes and the visible login form underneath
    // is what the user submits next, with their new password.
    [HttpPost("/login/setup-password"), ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> SetupPassword(SetupPasswordViewModel vm)
    {
        if (!ModelState.IsValid)
            return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToArray() });

        if (!_cache.TryCompletePasswordSetup(vm.Token, out var userId, out var email))
            return Json(new { success = false, errors = new[] { _L["Msg_SetupLinkExpired"].Value } });

        var ok = await _auth.SetPasswordAsync(userId, vm.NewPassword);
        if (!ok) return Json(new { success = false, errors = new[] { _L["Msg_SetupLinkExpired"].Value } });

        return Json(new { success = true, email });
    }

    [HttpGet("/login/complete")]
    public IActionResult CompleteLogin(string? token)
    {
        if (!HttpContext.CompleteSessionRotation(_cache, token)) return Redirect("/login");
        return RedirectToAction("Index", "Dashboard", new { area = "Home" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Redirect("/login");
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
        return Redirect("/login");
    }

    [HttpGet]
    [RequireUserAuth]
    public IActionResult ChangePassword()
    {
        ViewData["IsForced"] = HttpContext.Session.GetMustChangePassword();
        return View(new ChangePasswordViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequireUserAuth]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Redirect("/login");
        var wasForced = HttpContext.Session.GetMustChangePassword();
        ViewData["IsForced"] = wasForced;
        // A forced first-time change (temporary/OTP-issued password) skips the
        // Current Password field entirely — the user already proved they have
        // it by authenticating with it to reach this session.
        if (!wasForced && string.IsNullOrEmpty(vm.CurrentPassword))
            ModelState.AddModelError(nameof(vm.CurrentPassword), _L["Val_CurrentPasswordRequired"]);
        if (!ModelState.IsValid) return View(vm);
        var ok = wasForced
            ? await _auth.SetPasswordAsync(userId.Value, vm.NewPassword)
            : await _auth.ChangePasswordAsync(userId.Value, vm.CurrentPassword, vm.NewPassword);
        if (!ok) { ModelState.AddModelError("CurrentPassword", _L["Msg_CurrentPasswordIncorrect"]); return View(vm); }
        HttpContext.Session.SetMustChangePassword(false);
        TempData["Success"] = _L["Msg_PasswordChanged"].Value;
        // A forced first-time change (temporary password) goes straight into
        // the portal; a voluntary change from an already-active account
        // returns to this page so the success message has somewhere to show.
        return wasForced ? RedirectToAction("Index", "Dashboard", new { area = "Home" }) : RedirectToAction("ChangePassword");
    }
}
