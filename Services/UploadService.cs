using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;

    public UploadService(AppDbContext db) => _db = db;

    private static string Norm(IXLCell cell) => cell.GetString().Trim();

    private static DateOnly? SafeDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime)
        {
            var dt = cell.GetDateTime();
            return DateOnly.FromDateTime(dt);
        }
        var s = cell.GetString().Trim();
        if (DateOnly.TryParse(s, out var d)) return d;
        return null;
    }

    private static string Col(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return row.Cell(col.Address.ColumnNumber).GetString().Trim();
        }
        return "";
    }

    private static DateOnly? ColDate(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return SafeDate(row.Cell(col.Address.ColumnNumber));
        }
        return null;
    }

    private static void ValidateFile(IFormFile file)
    {
        const long maxBytes = 10 * 1024 * 1024; // 10 MB
        if (file.Length > maxBytes)
            throw new InvalidOperationException("File size exceeds the 10 MB limit.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
            throw new InvalidOperationException("Only Excel files (.xlsx / .xls) are allowed.");
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            _ => "application/octet-stream"
        };

    public async Task<(bool, string, int)> UploadActiveEmployeesAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        ValidateFile(file);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileBytes = ms.ToArray();
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        await _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year).ExecuteDeleteAsync();

        var records = new List<ActiveEmployee>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID", "id");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            records.Add(new ActiveEmployee
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = Col(row, ws, "Store", "store"),
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Grade = Col(row, ws, "Grade", "grade"),
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date"),
            });
        }

        if (records.Count > 0) await _db.ActiveEmployees.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    public async Task<(bool, string, int)> UploadResignationsAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        ValidateFile(file);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileBytes = ms.ToArray();
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        await _db.Resignations.Where(r => r.Month == month && r.Year == year).ExecuteDeleteAsync();

        var records = new List<Resignation>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            records.Add(new Resignation
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = Col(row, ws, "Store", "store"),
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date"),
                ResignationDate = ColDate(row, ws, "Resignation Date", "ResignationDate", "resignation_date", "Last Day"),
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
            });
        }

        if (records.Count > 0) await _db.Resignations.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "resignations", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    public async Task<(bool, string, int)> UploadStoreReferenceAsync(IFormFile file, int month, int year, string uploadedBy)
    {
        ValidateFile(file);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileBytes = ms.ToArray();
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        await _db.StoreReferences.Where(s => s.Month == month && s.Year == year).ExecuteDeleteAsync();

        var records = new List<StoreReference>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var storeName = Col(row, ws, "Store Name", "StoreName", "Store", "store_name");
            if (string.IsNullOrEmpty(storeName)) continue;

            records.Add(new StoreReference
            {
                Month = month, Year = year,
                StoreName = storeName,
                StoreLeader = Col(row, ws, "Store Leader", "StoreLeader", "store_leader"),
                OperationConsultant = Col(row, ws, "Operation Consultant", "OperationConsultant", "OC", "Consultant"),
                OperationManager = Col(row, ws, "Operation Manager", "OperationManager", "OM", "Manager"),
            });
        }

        if (records.Count > 0) await _db.StoreReferences.AddRangeAsync(records);
        _db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
        await _db.SaveChangesAsync();

        return (true, $"Processed {records.Count} records", records.Count);
    }

    private static string NormalizeHeader(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public async Task<(bool, string, int)> UploadExitInterviewsAsync(IFormFile file, string uploadedBy)
    {
        ValidateFile(file);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileBytes = ms.ToArray();
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        // Match by normalized header text (Microsoft Forms exports sometimes
        // include stray/non-breaking spaces inside question text) rather than
        // the fixed-alias Col() helper used by the other upload types.
        var headerMap = new Dictionary<string, int>();
        foreach (var cell in ws.Row(1).Cells())
        {
            var key = NormalizeHeader(cell.GetString());
            if (!string.IsNullOrEmpty(key)) headerMap[key] = cell.Address.ColumnNumber;
        }

        string Get(IXLRow row, string header) =>
            headerMap.TryGetValue(NormalizeHeader(header), out var col) ? row.Cell(col).GetString().Trim() : "";

        var parsed = new List<(string ResponseId, string EmployeeId, ExitInterview Row)>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var responseId = Get(row, "ID");
            var employeeId = Get(row, "الرقم الوظيفى");
            if (string.IsNullOrWhiteSpace(responseId) && string.IsNullOrWhiteSpace(employeeId)) continue;

            DateTime? completed = null;
            if (headerMap.TryGetValue(NormalizeHeader("Completion time"), out var completedCol))
            {
                var cell = row.Cell(completedCol);
                if (!cell.IsEmpty())
                {
                    if (cell.DataType == XLDataType.DateTime) completed = cell.GetDateTime();
                    else if (DateTime.TryParse(cell.GetString(), out var dt)) completed = dt;
                }
            }

            var interview = new ExitInterview
            {
                FormsResponseId = responseId,
                EmployeeId = employeeId,
                SubmittedAt = completed,
                Month = completed?.Month ?? 0,
                Year = completed?.Year ?? 0,

                ReasonForLeaving = Get(row, "برجاء اختيار سبب ترك العمل"),
                ReasonOtherText = NullIfEmpty(Get(row, "فى حالة وجود سبب اخر ( الرجاء ذكره )")),
                FairTreatment = Get(row, "هل يتم معاملة جميع العاملين معاملة عادلة ؟"),
                EncourageOpinions = Get(row, "يتم تشجيع العاملين على ابداء ارائهم و اقتراحاتهم"),
                ComplaintsHandling = Get(row, "يتم التعامل مع المشكلات و الشكاوى بطريقة فعالة"),
                BenefitsMatch = Get(row, "من وجههة نظرك هل المزايا التى تقدمها ماكدونالدز مصر تتفق مع متطلبات العمل ؟"),
                Teamwork = Get(row, "ما هو تقييمك لمستوى التعاون بين الزملاء في المطعم و هل يتم العمل بروح الفريق الواحد ؟"),
                Communication = Get(row, "كيف تقيم مدي التواصل بين المطاعم والإدارة؟"),
                OverallExperience = Get(row, "كيف تصف تجربتك الإجمالية للعمل داخل ماكدونالدز - مصر ؟"),
                TaskFit = Get(row, "هل تشعر بانه تم تكليفك بالمهام و المسئوليات المناسبة للوظيفة التى تم تعينك عليها ؟"),
                Training = Get(row, "هل حصلت على التدريب الكافى لمساعدتك على أداء عملك ؟"),
                Feedback = Get(row, "هل كنت تتلقى ملاحظات و توجيهات عن مستوى ادائك ؟"),
                UsePersonalAbilities = Get(row, "الى اى مدى اتيحت لك الفرصه فى استخدام قدراتك الشخصية اثناء عملك بالشركة ؟"),
                WouldReturn = Get(row, "هل تفكر في العودة للعمل معنا مرة أخرى؟"),
                WorkloadCondition = Get(row, "من وجهه نظرك : هل ظروف التشغيل فى المطعم تتسم ب :-"),
                WorkPressureReasonText = NullIfEmpty(Get(row, "فى حالة اختيارك ان مستوى ضغط العمل شديد الرجاء اختيار السبب ؟ ( برجاء توضيح السبب )")),
                WhatWouldChangeText = NullIfEmpty(Get(row, "لو كنت صاحب قرار في ماكدونالدز مصر ايه اول حاجة حابب تغيرها ؟")),
                WhatLearnedText = NullIfEmpty(Get(row, "حاجة اتعلمتها في ماكدونالدز مصر و هتبقي مفيدة ليك في المستقبل ؟")),
                FinalCommentsText = NullIfEmpty(Get(row, "هل هناك أي شيء ترغب في مشاركته معنا قبل مغادرتك؟")),
            };

            parsed.Add((responseId, employeeId, interview));
        }

        // Resolve Store / JobTitle from each employee's most recent resignation
        // record, then Store Leader / OC / OM from the store reference closest
        // to that resignation's period. Never stores or exposes the employee's
        // name or national ID.
        var employeeIds = parsed.Where(p => !string.IsNullOrWhiteSpace(p.EmployeeId)).Select(p => p.EmployeeId).Distinct().ToList();
        var resignations = employeeIds.Count == 0
            ? new List<Resignation>()
            : await _db.Resignations.Where(r => employeeIds.Contains(r.EmployeeId)).ToListAsync();
        var resignationByEmployee = resignations
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).First());

        var storeNames = resignationByEmployee.Values.Select(r => r.Store).Distinct().ToList();
        var storeRefs = storeNames.Count == 0
            ? new List<StoreReference>()
            : await _db.StoreReferences.Where(s => storeNames.Contains(s.StoreName)).ToListAsync();

        foreach (var (_, employeeId, interview) in parsed)
        {
            if (string.IsNullOrWhiteSpace(employeeId) || !resignationByEmployee.TryGetValue(employeeId, out var res)) continue;

            interview.Store = res.Store;
            interview.JobTitle = res.JobTitle;

            var refMatch = storeRefs.FirstOrDefault(s => s.StoreName == res.Store && s.Year == res.Year && s.Month == res.Month)
                ?? storeRefs.Where(s => s.StoreName == res.Store).OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).FirstOrDefault();
            if (refMatch != null)
            {
                interview.StoreLeader = refMatch.StoreLeader;
                interview.OperationConsultant = refMatch.OperationConsultant;
                interview.OperationManager = refMatch.OperationManager;
            }
        }

        // Upsert by Forms response id: re-exporting the full response history
        // from Microsoft Forms must not duplicate previously imported rows.
        var responseIds = parsed.Where(p => !string.IsNullOrWhiteSpace(p.ResponseId)).Select(p => p.ResponseId).ToList();
        if (responseIds.Count > 0)
            await _db.ExitInterviews.Where(e => responseIds.Contains(e.FormsResponseId)).ExecuteDeleteAsync();
        if (parsed.Count > 0)
            await _db.ExitInterviews.AddRangeAsync(parsed.Select(p => p.Row));

        var now = DateTime.UtcNow;
        _db.UploadLogs.Add(new UploadLog { FileType = "exit_interviews", FileName = file.FileName, Month = now.Month, Year = now.Year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
        await _db.SaveChangesAsync();

        return (true, $"Processed {parsed.Count} exit interview responses", parsed.Count);
    }

    private static readonly System.Linq.Expressions.Expression<Func<UploadLog, UploadLog>> ProjectWithoutFile = l => new UploadLog
    {
        Id = l.Id, FileType = l.FileType, FileName = l.FileName,
        Month = l.Month, Year = l.Year, UploadDate = l.UploadDate,
        UploadedBy = l.UploadedBy, HasFile = l.FileContent != null
    };

    public async Task<List<UploadLog>> GetLogsAsync() =>
        await _db.UploadLogs.OrderByDescending(l => l.UploadDate).Select(ProjectWithoutFile).ToListAsync();

    public async Task<(List<UploadLog> Items, int TotalCount)> GetLogsPagedAsync(int page, int pageSize)
    {
        var q = _db.UploadLogs.OrderByDescending(l => l.UploadDate);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(ProjectWithoutFile).ToListAsync();
        return (items, total);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id)
    {
        var log = await _db.UploadLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log?.FileContent == null) return null;
        return (log.FileContent, log.ContentType ?? "application/octet-stream", log.FileName);
    }

    public async Task DeleteLogAsync(int id)
    {
        var log = await _db.UploadLogs.FindAsync(id);
        if (log == null) return;

        if (log.FileType == "active_employees")
            await _db.ActiveEmployees.Where(e => e.Month == log.Month && e.Year == log.Year).ExecuteDeleteAsync();
        else if (log.FileType == "resignations")
            await _db.Resignations.Where(r => r.Month == log.Month && r.Year == log.Year).ExecuteDeleteAsync();
        else if (log.FileType == "store_reference")
            await _db.StoreReferences.Where(s => s.Month == log.Month && s.Year == log.Year).ExecuteDeleteAsync();
        // exit_interviews is intentionally excluded: each upload is a full,
        // cumulative Forms export upserted by response id, not a single
        // month/year snapshot, so deleting one log entry must not wipe data.

        _db.UploadLogs.Remove(log);
        await _db.SaveChangesAsync();
    }
}
