using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _auth;

    public AccountController(IAuthService auth) => _auth = auth;

    [HttpGet]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetUserId() != null)
            return RedirectToAction("Index", "Home");
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null)
        {
            ModelState.AddModelError("", "Invalid email or password");
            return View(vm);
        }

        HttpContext.Session.SetUserSession(user.Id, user.Email, user.Role, user.AssignedName);

        if (user.Role == "Admin_Full" || user.Role == "Admin_Read")
            return RedirectToAction("Analytics", "Admin");

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}
