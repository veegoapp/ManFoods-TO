using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Areas.Admin.Controllers;

[Area("Admin")]
[RequireAdminAuth]
public class DashboardController : Controller
{
    private readonly IUploadService _uploads;
    private readonly IUserService _users;
    private readonly IDashboardService _dashboard;
    private readonly IStoreService _stores;
    private readonly IOtpService _otp;

    public DashboardController(IUploadService uploads, IUserService users, IDashboardService dashboard, IStoreService stores, IOtpService otp)
    {
        _uploads = uploads;
        _users = users;
        _dashboard = dashboard;
        _stores = stores;
        _otp = otp;
    }

    public IActionResult Turnover() => View();

    public IActionResult Workforce() => View();

    public IActionResult Retention() => View();

    public IActionResult Stores() => View();

    public IActionResult ExitInterviews() => View();

    public IActionResult NinetyDayTurnover() => View();

    public IActionResult AiAssistant() => View();

    public IActionResult EarlyWarning() => View();

    public IActionResult Scorecard() => View();

    public IActionResult Targets() => View();

    public async Task<IActionResult> StoreProfile(string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return RedirectToAction("Turnover");

        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var storeRefs = (await _stores.GetStoresAsync(null, null, role, assignedName))
            .Where(s => s.StoreName == store)
            .ToList();
        if (!storeRefs.Any()) return NotFound();

        var latest = storeRefs.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First();
        ViewBag.StoreName = store;
        ViewBag.StoreLeader = latest.StoreLeader;
        ViewBag.OperationConsultant = latest.OperationConsultant;
        ViewBag.OperationManager = latest.OperationManager;
        return View();
    }

    public async Task<IActionResult> Reports()
    {
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        return View(periods);
    }

    [HttpGet("admin/dashboard/export")]
    public async Task<IActionResult> Export(int month, int year, string reportType = "summary")
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();

        using var wb = new XLWorkbook();
        string fileName;

        if (reportType == "stores")
        {
            fileName = $"Store_Comparison_{year}_{month:D2}.xlsx";
            var rows = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName);
            var ws = wb.AddWorksheet("Store Comparison");
            var headers = new[] { "Store", "OC", "OM", "Headcount", "New Hires", "Resignations", "Turnover %" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(1, i + 1);
                c.Value = headers[i]; c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                c.Style.Font.FontColor = XLColor.White;
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                ws.Cell(r + 2, 1).Value = row.StoreName;
                ws.Cell(r + 2, 2).Value = row.OperationConsultant;
                ws.Cell(r + 2, 3).Value = row.OperationManager;
                ws.Cell(r + 2, 4).Value = row.Headcount;
                ws.Cell(r + 2, 5).Value = row.NewHires;
                ws.Cell(r + 2, 6).Value = row.Resignations;
                ws.Cell(r + 2, 7).Value = row.TurnoverRate;
            }
            ws.Columns().AdjustToContents();
        }
        else
        {
            fileName = $"Summary_Report_{year}_{month:D2}.xlsx";
            var kpi = await _dashboard.GetKpisAsync(month, year, null, role, assignedName);
            var jobTitle = await _dashboard.GetTurnoverByJobTitleAsync(month, year, null, role, assignedName);
            var tenure = await _dashboard.GetTurnoverByTenureAsync(month, year, null, role, assignedName);
            var gender = await _dashboard.GetGenderBreakdownAsync(month, year, null, role, assignedName);

            var ws1 = wb.AddWorksheet("Summary KPIs");
            ws1.Cell(1, 1).Value = "Metric"; ws1.Cell(1, 2).Value = "Value";
            ws1.Row(1).Style.Font.Bold = true;
            ws1.Cell(2, 1).Value = "Total Headcount"; ws1.Cell(2, 2).Value = kpi.TotalHeadcount;
            ws1.Cell(3, 1).Value = "New Hires"; ws1.Cell(3, 2).Value = kpi.NewHires;
            ws1.Cell(4, 1).Value = "Total Resignations"; ws1.Cell(4, 2).Value = kpi.TotalResignations;
            ws1.Cell(5, 1).Value = "Turnover Rate (%)"; ws1.Cell(5, 2).Value = kpi.TurnoverRate;
            ws1.Columns().AdjustToContents();

            var ws2 = wb.AddWorksheet("By Job Title");
            ws2.Cell(1, 1).Value = "Job Title"; ws2.Cell(1, 2).Value = "Resignations";
            ws2.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < jobTitle.Count; i++) { ws2.Cell(i + 2, 1).Value = jobTitle[i].Label; ws2.Cell(i + 2, 2).Value = jobTitle[i].Value; }
            ws2.Columns().AdjustToContents();

            var ws3 = wb.AddWorksheet("By Tenure");
            ws3.Cell(1, 1).Value = "Tenure Bucket"; ws3.Cell(1, 2).Value = "Resignations";
            ws3.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < tenure.Count; i++) { ws3.Cell(i + 2, 1).Value = tenure[i].Label; ws3.Cell(i + 2, 2).Value = tenure[i].Value; }
            ws3.Columns().AdjustToContents();

            var ws4 = wb.AddWorksheet("By Gender");
            ws4.Cell(1, 1).Value = "Gender"; ws4.Cell(1, 2).Value = "Resignations";
            ws4.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < gender.Count; i++) { ws4.Cell(i + 2, 1).Value = gender[i].Label; ws4.Cell(i + 2, 2).Value = gender[i].Value; }
            ws4.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public async Task<IActionResult> Uploads(int page = 1)
    {
        const int pageSize = 10;
        var (items, total) = await _uploads.GetLogsPagedAsync(page, pageSize);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
        ViewBag.TotalCount = total;
        return View(items);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadActiveEmployees(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadActiveEmployeesAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadResignations(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadResignationsAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadStoreReference(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadStoreReferenceAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadExitInterviews(MvcApp.Models.ViewModels.ExitInterviewUploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadExitInterviewsAsync(vm.File, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpGet("admin/dashboard/download-template")]
    public IActionResult DownloadTemplate([FromQuery] string type)
    {
        using var wb = new XLWorkbook();
        string fileName;

        if (type == "active_employees")
        {
            fileName = "Template_Active_Employees.xlsx";
            var ws = wb.AddWorksheet("Active Employees");
            var headers = new[] { "Employee ID", "Name", "Store", "Job Title", "Grade", "Payroll Group", "Cost Center", "Gender", "Hire Date" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "EMP001"; ws.Cell(2, 2).Value = "Ahmed Mohamed";
            ws.Cell(2, 3).Value = "Store 1"; ws.Cell(2, 4).Value = "Crew Member";
            ws.Cell(2, 5).Value = "L1"; ws.Cell(2, 6).Value = "Group A";
            ws.Cell(2, 7).Value = "CC001"; ws.Cell(2, 8).Value = "Male";
            ws.Cell(2, 9).Value = "2023-01-15";
            ws.Cell(3, 1).Value = "EMP002"; ws.Cell(3, 2).Value = "Sara Ali";
            ws.Cell(3, 3).Value = "Store 2"; ws.Cell(3, 4).Value = "Shift Manager";
            ws.Cell(3, 5).Value = "L3"; ws.Cell(3, 6).Value = "Group B";
            ws.Cell(3, 7).Value = "CC002"; ws.Cell(3, 8).Value = "Female";
            ws.Cell(3, 9).Value = "2022-06-01";
            ws.Columns().AdjustToContents();
        }
        else if (type == "resignations")
        {
            fileName = "Template_Resignations.xlsx";
            var ws = wb.AddWorksheet("Resignations");
            var headers = new[] { "Employee ID", "Name", "Store", "Job Title", "Gender", "Hire Date", "Resignation Date", "Payroll Group", "Cost Center" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "EMP010"; ws.Cell(2, 2).Value = "Mohamed Hassan";
            ws.Cell(2, 3).Value = "Store 1"; ws.Cell(2, 4).Value = "Crew Member";
            ws.Cell(2, 5).Value = "Male"; ws.Cell(2, 6).Value = "2023-03-01";
            ws.Cell(2, 7).Value = "2025-05-20"; ws.Cell(2, 8).Value = "Group A";
            ws.Cell(2, 9).Value = "CC001";
            ws.Cell(3, 1).Value = "EMP011"; ws.Cell(3, 2).Value = "Nour Khaled";
            ws.Cell(3, 3).Value = "Store 3"; ws.Cell(3, 4).Value = "Cashier";
            ws.Cell(3, 5).Value = "Female"; ws.Cell(3, 6).Value = "2024-01-10";
            ws.Cell(3, 7).Value = "2025-05-28"; ws.Cell(3, 8).Value = "Group C";
            ws.Cell(3, 9).Value = "CC003";
            ws.Columns().AdjustToContents();
        }
        else if (type == "store_reference")
        {
            fileName = "Template_Store_Reference.xlsx";
            var ws = wb.AddWorksheet("Store Reference");
            var headers = new[] { "Store Name", "Store Leader", "Operation Consultant", "Operation Manager" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "Store 1"; ws.Cell(2, 2).Value = "Khaled Ibrahim";
            ws.Cell(2, 3).Value = "Ahmed Samy"; ws.Cell(2, 4).Value = "Mohamed Nour";
            ws.Cell(3, 1).Value = "Store 2"; ws.Cell(3, 2).Value = "Sara Hassan";
            ws.Cell(3, 3).Value = "Mona Ali"; ws.Cell(3, 4).Value = "Mohamed Nour";
            ws.Cell(4, 1).Value = "Store 3"; ws.Cell(4, 2).Value = "Omar Tarek";
            ws.Cell(4, 3).Value = "Ahmed Samy"; ws.Cell(4, 4).Value = "Fatma Reda";
            ws.Columns().AdjustToContents();
        }
        else if (type == "exit_interviews")
        {
            // Mirrors the real Microsoft Forms export shape (headers match the
            // question text exactly) so admins know what to upload — this is a
            // reference sample, not a fixed template to fill in by hand.
            fileName = "Sample_Exit_Interviews.xlsx";
            var ws = wb.AddWorksheet("Exit Interview Responses");
            var headers = new[]
            {
                "ID", "Start time", "Completion time", "Email", "Name", "Last modified time",
                "الرقم الوظيفى",
                "الاسم ( برجاء كتابة الاسم ثلاثى )",
                "الرقم القومى ( يرجى ادخال ال 14 رقم )",
                "برجاء اختيار سبب ترك العمل",
                "فى حالة وجود سبب اخر ( الرجاء ذكره )",
                "هل يتم معاملة جميع العاملين معاملة عادلة ؟",
                "يتم تشجيع العاملين على ابداء ارائهم و اقتراحاتهم",
                "يتم التعامل مع المشكلات و الشكاوى بطريقة فعالة",
                "من وجههة نظرك هل المزايا التى تقدمها ماكدونالدز مصر تتفق مع متطلبات العمل ؟",
                "ما هو تقييمك لمستوى التعاون بين الزملاء في المطعم و هل يتم العمل بروح الفريق الواحد ؟",
                "كيف تقيم مدي التواصل بين المطاعم والإدارة؟",
                "كيف تصف تجربتك الإجمالية للعمل داخل ماكدونالدز - مصر ؟",
                "هل تشعر بانه تم تكليفك بالمهام و المسئوليات المناسبة للوظيفة التى تم تعينك عليها ؟",
                "هل حصلت على التدريب الكافى لمساعدتك على أداء عملك ؟",
                "هل كنت تتلقى ملاحظات و توجيهات عن مستوى ادائك ؟",
                "الى اى مدى اتيحت لك الفرصه فى استخدام قدراتك الشخصية اثناء عملك بالشركة ؟",
                "هل تفكر في العودة للعمل معنا مرة أخرى؟",
                "من وجهه نظرك : هل ظروف التشغيل فى المطعم تتسم ب :-",
                "فى حالة اختيارك ان مستوى ضغط العمل شديد الرجاء اختيار السبب ؟ ( برجاء توضيح السبب )",
                "لو كنت صاحب قرار في ماكدونالدز مصر ايه اول حاجة حابب تغيرها ؟",
                "حاجة اتعلمتها في ماكدونالدز مصر و هتبقي مفيدة ليك في المستقبل ؟",
                "هل هناك أي شيء ترغب في مشاركته معنا قبل مغادرتك؟",
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            var sample = new[]
            {
                "1355", "2026-06-25 10:17", "2026-06-29 13:49", "anonymous", "", "",
                "38416", "احمد ماهر عبدالعزيز", "29508172103033",
                "المرتب غير مجزى", "عدم توافر فرص الترقيه",
                "أعارض", "أعارض بشدة", "لا أوافق ولا اعارض", "أعارض بشدة",
                "جيدة", "مقبولة", "ضعيفة",
                "لا", "لا", "لا", "بدرجة ضعيفة",
                "ربما فى المستقبل", "ضغط عمل بشكل مستمر", "لا اتمكن من الحصول على الاجازات السنوية",
                "مديرالتدريب", "الالتزام", "لا",
            };
            for (int i = 0; i < sample.Length; i++) ws.Cell(2, i + 1).Value = sample[i];
            ws.Columns().AdjustToContents();
        }
        else if (type == "bulk_users")
        {
            fileName = "Template_Bulk_Users.xlsx";
            var ws = wb.AddWorksheet("Users");
            var headers = new[] { "Email", "Phone" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "ahmed@manfoods.com"; ws.Cell(2, 2).Value = "+201012345678";
            ws.Cell(3, 1).Value = "sara@manfoods.com"; ws.Cell(3, 2).Value = "+201098765432";
            ws.Columns().AdjustToContents();
        }
        else
        {
            return NotFound();
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public async Task<IActionResult> DownloadUploadFile(int id)
    {
        var file = await _uploads.GetFileAsync(id);
        if (file == null) return NotFound();
        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUploadLog(int id)
    {
        await _uploads.DeleteLogAsync(id);
        TempData["Success"] = "Upload log and associated data deleted.";
        return RedirectToAction("Uploads");
    }

    public async Task<IActionResult> Users()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    public IActionResult CreateUser() => View(new MvcApp.Models.ViewModels.CreateUserViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(MvcApp.Models.ViewModels.CreateUserViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _users.CreateAsync(vm);
        TempData["Success"] = "User created successfully.";
        return RedirectToAction("Users");
    }

    public async Task<IActionResult> EditUser(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null) return NotFound();
        return View(new MvcApp.Models.ViewModels.EditUserViewModel { Id = user.Id, Email = user.Email, Phone = user.Phone, Role = user.Role });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, MvcApp.Models.ViewModels.EditUserViewModel vm)
    {
        vm.Id = id;
        if (!ModelState.IsValid) return View(vm);
        await _users.UpdateAsync(id, vm);
        TempData["Success"] = "User updated.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _users.DeleteAsync(id);
        TempData["Success"] = "User deleted.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadBulkUsers(MvcApp.Models.ViewModels.BulkUserUploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file."; return RedirectToAction("Users"); }
        try
        {
            var (created, skipped) = await _users.UploadBulkUsersAsync(vm.File);
            TempData["Success"] = $"Created {created} pending user(s)." + (skipped > 0 ? $" Skipped {skipped} (already existed)." : "");
        }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Users");
    }

    public async Task<IActionResult> GenerateBulkOtps()
    {
        var (count, bytes) = await _otp.GenerateBulkOtpsAsync();
        if (count == 0) { TempData["Error"] = "No pending users need an OTP right now."; return RedirectToAction("Users"); }
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Bulk_OTPs_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateOtp(int id)
    {
        var otp = await _otp.GenerateSingleOtpAsync(id);
        if (otp == null) return NotFound();
        return Json(new { otp });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateRecoveryKey([FromForm] string password)
    {
        var email = HttpContext.Session.GetEmail();
        var key = await _users.RegenerateRecoveryKeyAsync(email, password);
        if (key == null) return Json(new { error = "Incorrect password." });
        return Json(new { key });
    }
}
