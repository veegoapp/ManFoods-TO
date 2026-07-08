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
    private readonly IReportService _reports;

    public DashboardController(IUploadService uploads, IUserService users, IDashboardService dashboard, IStoreService stores, IOtpService otp, IReportService reports)
    {
        _uploads = uploads;
        _users = users;
        _dashboard = dashboard;
        _stores = stores;
        _otp = otp;
        _reports = reports;
    }

    public IActionResult Turnover() => View();

    public IActionResult Comparisons() => View();

    public IActionResult Workforce() => View();

    public IActionResult Retention() => View();

    public IActionResult Stores() => View();

    public IActionResult ExitInterviews() => View();

    public IActionResult NinetyDayTurnover() => View();

    public IActionResult AiAssistant() => View();

    [RequireAdminAuth]
    public IActionResult AiUsage() => View();

    public IActionResult EarlyWarning() => View();

    public IActionResult Scorecard() => View();

    public async Task<IActionResult> Reports()
    {
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        return View(periods);
    }

    [HttpGet("admin/dashboard/reports/{reportType}")]
    public async Task<IActionResult> ReportDetail(string reportType)
    {
        if (MvcApp.Models.ViewModels.ReportCatalog.Find(reportType) == null) return NotFound();

        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        var periods = await _dashboard.GetAvailablePeriodsAsync();
        var stores = await _stores.GetStoresAsync(null, null, role, assignedName);
        ViewBag.Stores = stores.Select(s => s.StoreName).Distinct().OrderBy(s => s).ToList();
        ViewBag.OperationManagers = await _dashboard.GetOperationManagersAsync(null, null);
        ViewBag.OperationConsultants = await _dashboard.GetOperationConsultantsAsync(null, null);
        ViewBag.ReportType = reportType;
        return View(periods);
    }

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private async Task<IActionResult> DownloadWorkbookAsync(XLWorkbook wb, string fileName)
    {
        using (wb)
        {
            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            return File(stream.ToArray(), XlsxContentType, fileName);
        }
    }

    [HttpGet("admin/dashboard/export")]
    public async Task<IActionResult> Export(int month, int year, string reportType = "summary",
        string? store = null, string? om = null, string? oc = null, string? months = null)
    {
        var role = HttpContext.Session.GetRole();
        var assignedName = HttpContext.Session.GetAssignedName();
        store = string.IsNullOrWhiteSpace(store) ? null : store;
        om = string.IsNullOrWhiteSpace(om) ? null : om;
        oc = string.IsNullOrWhiteSpace(oc) ? null : oc;
        months = string.IsNullOrWhiteSpace(months) ? null : months;

        switch (reportType)
        {
            case "stores":
                return await DownloadWorkbookAsync(
                    await _reports.BuildStoreComparisonReportAsync(month, year, role, assignedName, om, oc),
                    $"Store_Comparison_{year}_{month:D2}.xlsx");
            case "ninety-day":
                return await DownloadWorkbookAsync(await _reports.BuildNinetyDayReportAsync(store), "90_Day_Turnover_Report.xlsx");
            case "retention":
                return await DownloadWorkbookAsync(await _reports.BuildRetentionReportAsync(store), "Retention_Report.xlsx");
            case "exit-interviews":
                return await DownloadWorkbookAsync(await _reports.BuildExitInterviewReportAsync(store, om, oc), "Exit_Interview_Report.xlsx");
            case "scorecard":
                return await DownloadWorkbookAsync(await _reports.BuildScorecardReportAsync(om, oc), "Scorecard_Report.xlsx");
            case "early-warning":
                return await DownloadWorkbookAsync(await _reports.BuildEarlyWarningReportAsync(store), "Early_Warning_Report.xlsx");
            case "trend-matrix":
                return await DownloadWorkbookAsync(
                    await _reports.BuildTrendMatrixReportAsync(role, assignedName, om, oc, year > 0 ? year : null, months),
                    $"Turnover_Trend_Matrix_{year}.xlsx");
            case "ninety-day-trend-matrix":
                return await DownloadWorkbookAsync(
                    await _reports.BuildNinetyDayTrendMatrixReportAsync(om, oc, months, year > 0 ? year : null),
                    "90_Day_Trend_Matrix_Report.xlsx");
            default:
                return await DownloadWorkbookAsync(
                    await _reports.BuildSummaryReportAsync(month, year, role, assignedName, store),
                    $"Summary_Report_{year}_{month:D2}.xlsx");
        }
    }

    [RequireAdminAuth]
    public async Task<IActionResult> Uploads(int page = 1)
    {
        const int pageSize = 10;
        var (items, total) = await _uploads.GetHistoryPagedAsync(page, pageSize);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
        ViewBag.TotalCount = total;
        return View(items);
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> UploadPeriodData(MvcApp.Models.ViewModels.PeriodUploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.ActiveEmployeesFile == null || vm.ResignationsFile == null || vm.StoreReferenceFile == null)
        {
            TempData["Error"] = "الرجاء رفع الثلاث ملفات (الأكتيف ليست، الاستقالات، ومرجع الفروع) مع تحديد الشهر والسنة — الثلاثة مطلوبين معًا.";
            return RedirectToAction("Uploads");
        }
        try
        {
            var email = HttpContext.Session.GetEmail();
            var (_, msg, _) = await _uploads.UploadPeriodDataAsync(vm.ActiveEmployeesFile, vm.ResignationsFile, vm.StoreReferenceFile, vm.Month, vm.Year, email);
            TempData["Success"] = msg;
        }
        catch { TempData["Error"] = "Upload failed. Please check the file format and try again."; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> UpdatePeriodFile(MvcApp.Models.ViewModels.UpdateSingleFileViewModel vm)
    {
        var validTypes = new[] { "active_employees", "resignations", "store_reference" };
        if (!ModelState.IsValid || vm.File == null || !validTypes.Contains(vm.FileType))
        {
            TempData["Error"] = "الرجاء اختيار نوع الملف وتحديد ملف إكسيل صحيح.";
            return RedirectToAction("Uploads");
        }
        try
        {
            var email = HttpContext.Session.GetEmail();
            var (_, msg) = await _uploads.UpdateSingleFileAsync(vm.FileType, vm.Month, vm.Year, vm.File, email);
            TempData["Success"] = msg;
        }
        catch { TempData["Error"] = "Upload failed. Please check the file format and try again."; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> UploadExitInterviews(MvcApp.Models.ViewModels.ExitInterviewUploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadExitInterviewsAsync(vm.File, email); TempData["Success"] = msg; }
        catch { TempData["Error"] = "Upload failed. Please check the file format and try again."; }
        return RedirectToAction("Uploads");
    }

    [HttpGet("admin/dashboard/download-template")]
    [RequireAdminAuth]
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

    [RequireAdminAuth]
    public async Task<IActionResult> DownloadUploadFile(int id)
    {
        var file = await _uploads.GetFileAsync(id);
        if (file == null) return NotFound();
        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpGet("admin/dashboard/download-upload-group")]
    [RequireAdminAuth]
    public async Task<IActionResult> DownloadUploadGroup([FromQuery] int id)
    {
        var files = await _uploads.ExportGroupAsync(id);
        if (files.Count == 0) return NotFound();

        if (files.Count == 1)
            return File(files[0].Content, files[0].ContentType, files[0].FileName);

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in files)
            {
                var entry = zip.CreateEntry(f.FileName, System.IO.Compression.CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(f.Content);
            }
        }
        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", $"Upload_{DateTime.UtcNow:yyyyMMdd_HHmm}.zip");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> DeleteUploadLog(int id)
    {
        await _uploads.DeleteLogAsync(id);
        TempData["Success"] = "Upload log and associated data deleted.";
        return RedirectToAction("Uploads");
    }

    [RequireAdminAuth]
    public async Task<IActionResult> Users()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    [RequireAdminAuth]
    public IActionResult CreateUser() => View(new MvcApp.Models.ViewModels.CreateUserViewModel());

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> CreateUser(MvcApp.Models.ViewModels.CreateUserViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _users.CreateAsync(vm);
        TempData["Success"] = "User created successfully.";
        return RedirectToAction("Users");
    }

    [RequireAdminAuth]
    public async Task<IActionResult> EditUser(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null) return NotFound();
        return View(new MvcApp.Models.ViewModels.EditUserViewModel { Id = user.Id, Email = user.Email, Phone = user.Phone, Role = user.Role });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> EditUser(int id, MvcApp.Models.ViewModels.EditUserViewModel vm)
    {
        vm.Id = id;
        if (!ModelState.IsValid) return View(vm);
        await _users.UpdateAsync(id, vm);
        TempData["Success"] = "User updated.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _users.DeleteAsync(id);
        TempData["Success"] = "User deleted.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> UploadBulkUsers(MvcApp.Models.ViewModels.BulkUserUploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file."; return RedirectToAction("Users"); }
        try
        {
            var (created, skipped) = await _users.UploadBulkUsersAsync(vm.File);
            TempData["Success"] = $"Created {created} pending user(s)." + (skipped > 0 ? $" Skipped {skipped} (already existed)." : "");
        }
        catch { TempData["Error"] = "Upload failed. Please check the file format and try again."; }
        return RedirectToAction("Users");
    }

    [RequireAdminAuth]
    public async Task<IActionResult> GenerateBulkOtps()
    {
        var (count, bytes) = await _otp.GenerateBulkOtpsAsync();
        if (count == 0) { TempData["Error"] = "No pending users need an OTP right now."; return RedirectToAction("Users"); }
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Bulk_OTPs_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> GenerateOtp(int id)
    {
        var otp = await _otp.GenerateSingleOtpAsync(id);
        if (otp == null) return NotFound();
        return Json(new { otp });
    }

    [HttpPost, ValidateAntiForgeryToken, RequireAdminAuth]
    public async Task<IActionResult> RegenerateRecoveryKey([FromForm] string password)
    {
        var email = HttpContext.Session.GetEmail();
        var key = await _users.RegenerateRecoveryKeyAsync(email, password);
        if (key == null) return Json(new { error = "Incorrect password." });
        return Json(new { key });
    }
}
