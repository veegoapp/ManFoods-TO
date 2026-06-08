using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers;

[RequireAuth]
public class AdminController : Controller
{
    private readonly IUploadService _uploads;
    private readonly IUserService _users;

    public AdminController(IUploadService uploads, IUserService users)
    {
        _uploads = uploads;
        _users = users;
    }

    [RequireRole("Admin_Full", "Admin_Read")]
    public IActionResult Analytics() => View();

    [RequireRole("Admin_Full", "Admin_Read")]
    public async Task<IActionResult> Uploads()
    {
        var logs = await _uploads.GetLogsAsync();
        return View(logs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full", "Admin_Read")]
    public async Task<IActionResult> UploadActiveEmployees(UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null)
        {
            TempData["Error"] = "Please select a file and specify month/year.";
            return RedirectToAction("Uploads");
        }
        try
        {
            var email = HttpContext.Session.GetEmail();
            var (_, msg, _) = await _uploads.UploadActiveEmployeesAsync(vm.File, vm.Month, vm.Year, email);
            TempData["Success"] = msg;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Upload failed: {ex.Message}";
        }
        return RedirectToAction("Uploads");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full", "Admin_Read")]
    public async Task<IActionResult> UploadResignations(UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null)
        {
            TempData["Error"] = "Please select a file and specify month/year.";
            return RedirectToAction("Uploads");
        }
        try
        {
            var email = HttpContext.Session.GetEmail();
            var (_, msg, _) = await _uploads.UploadResignationsAsync(vm.File, vm.Month, vm.Year, email);
            TempData["Success"] = msg;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Upload failed: {ex.Message}";
        }
        return RedirectToAction("Uploads");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full", "Admin_Read")]
    public async Task<IActionResult> UploadStoreReference(UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null)
        {
            TempData["Error"] = "Please select a file and specify month/year.";
            return RedirectToAction("Uploads");
        }
        try
        {
            var email = HttpContext.Session.GetEmail();
            var (_, msg, _) = await _uploads.UploadStoreReferenceAsync(vm.File, vm.Month, vm.Year, email);
            TempData["Success"] = msg;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Upload failed: {ex.Message}";
        }
        return RedirectToAction("Uploads");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> DeleteUploadLog(int id)
    {
        await _uploads.DeleteLogAsync(id);
        TempData["Success"] = "Upload log and associated data deleted.";
        return RedirectToAction("Uploads");
    }

    [RequireRole("Admin_Full", "Admin_Read")]
    public async Task<IActionResult> Users()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    [RequireRole("Admin_Full")]
    public async Task<IActionResult> CreateUser()
    {
        var (managers, consultants) = await _users.GetAssignableNamesAsync();
        ViewBag.Managers = managers;
        ViewBag.Consultants = consultants;
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> CreateUser(CreateUserViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var (managers, consultants) = await _users.GetAssignableNamesAsync();
            ViewBag.Managers = managers;
            ViewBag.Consultants = consultants;
            return View(vm);
        }
        await _users.CreateAsync(vm);
        TempData["Success"] = "User created successfully.";
        return RedirectToAction("Users");
    }

    [RequireRole("Admin_Full")]
    public async Task<IActionResult> EditUser(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null) return NotFound();

        var (managers, consultants) = await _users.GetAssignableNamesAsync();
        ViewBag.Managers = managers;
        ViewBag.Consultants = consultants;

        return View(new EditUserViewModel
        {
            Id = user.Id, Email = user.Email,
            Role = user.Role, AssignedName = user.AssignedName
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> EditUser(int id, EditUserViewModel vm)
    {
        vm.Id = id;
        if (!ModelState.IsValid)
        {
            var (managers, consultants) = await _users.GetAssignableNamesAsync();
            ViewBag.Managers = managers;
            ViewBag.Consultants = consultants;
            return View(vm);
        }
        await _users.UpdateAsync(id, vm);
        TempData["Success"] = "User updated successfully.";
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _users.DeleteAsync(id);
        TempData["Success"] = "User deleted.";
        return RedirectToAction("Users");
    }

    [RequireRole("Admin_Full", "Admin_Read")]
    public IActionResult Turnover() => View();

    [RequireRole("Admin_Full", "Admin_Read")]
    public IActionResult Reports() => View();
}
