using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IStoreActionPlanService _actionPlans;

    private static readonly HashSet<string> PeriodFileTypes = new() { "active_employees", "resignations", "store_reference" };

    public UploadService(AppDbContext db, IStoreAccessService storeAccess, IStoreActionPlanService actionPlans)
    {
        _db = db;
        _storeAccess = storeAccess;
        _actionPlans = actionPlans;
    }

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

    private static async Task<byte[]> ReadBytesAsync(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static List<ActiveEmployee> ParseActiveEmployees(byte[] fileBytes, int month, int year)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

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
        return records;
    }

    private static List<Resignation> ParseResignations(byte[] fileBytes, int month, int year)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

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
        return records;
    }

    private static List<StoreReference> ParseStoreReference(byte[] fileBytes, int month, int year)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

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
                OperationManagerEmail = Col(row, ws, "Operation Manager Email", "OperationManagerEmail", "OM Email", "Manager Email").Trim().ToLowerInvariant(),
                OperationConsultantEmail = Col(row, ws, "Operation Consultant Email", "OperationConsultantEmail", "OC Email", "Consultant Email").Trim().ToLowerInvariant(),
                HeadManager = Col(row, ws, "Head Manager", "HeadManager", "HM"),
                HeadManagerEmail = Col(row, ws, "Head Manager Email", "HeadManagerEmail", "HM Email").Trim().ToLowerInvariant(),
            });
        }
        return records;
    }

    // Guards the one authorization-critical invariant of this table: a store
    // can only have ONE Operation Consultant / Operation Manager / Head
    // Manager email per (Month, Year), because StoreAccessService grants
    // access to any user whose email matches ANY row for that store — an
    // in-file duplicate (e.g. a copy-paste mistake) could otherwise hand the
    // same store to two different people for the same period. Rejects the
    // whole upload rather than silently keeping one row, since choosing which
    // duplicate is "correct" is not something this code can safely guess.
    private static void ValidateNoDuplicateStoreReferences(List<StoreReference> storeRecords, int month, int year)
    {
        var duplicates = storeRecords
            .GroupBy(s => s.StoreName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(s => s)
            .ToList();

        if (duplicates.Count > 0)
            throw new DuplicateStoreReferenceException(
                $"Duplicate store reference found for Store + Month + Year: {string.Join(", ", duplicates)} " +
                $"appear more than once for {month:D2}/{year}. Remove the duplicate row(s) from the file and re-upload.");
    }

    public async Task<(bool, string, Dictionary<string, int>)> UploadPeriodDataAsync(
        IFormFile activeEmployeesFile, IFormFile resignationsFile, IFormFile storeReferenceFile,
        int month, int year, string uploadedBy)
    {
        ValidateFile(activeEmployeesFile);
        ValidateFile(resignationsFile);
        ValidateFile(storeReferenceFile);

        var activeBytes = await ReadBytesAsync(activeEmployeesFile);
        var resignBytes = await ReadBytesAsync(resignationsFile);
        var storeBytes = await ReadBytesAsync(storeReferenceFile);

        // Parsed and validated before the transaction opens — a bad workbook or
        // an in-file duplicate throws here and nothing has touched the
        // database, so partial uploads are impossible.
        var activeRecords = ParseActiveEmployees(activeBytes, month, year);
        var resignRecords = ParseResignations(resignBytes, month, year);
        var storeRecords = ParseStoreReference(storeBytes, month, year);
        ValidateNoDuplicateStoreReferences(storeRecords, month, year);

        await using var tx = await _db.Database.BeginTransactionAsync();

        await _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year).ExecuteDeleteAsync();
        await _db.Resignations.Where(r => r.Month == month && r.Year == year).ExecuteDeleteAsync();
        await _db.StoreReferences.Where(s => s.Month == month && s.Year == year).ExecuteDeleteAsync();
        // Re-uploading the same period replaces its log entries too, so the
        // history table always shows exactly one current set of files per month.
        await _db.UploadLogs.Where(l => PeriodFileTypes.Contains(l.FileType) && l.Month == month && l.Year == year).ExecuteDeleteAsync();

        if (activeRecords.Count > 0) await _db.ActiveEmployees.AddRangeAsync(activeRecords);
        if (resignRecords.Count > 0) await _db.Resignations.AddRangeAsync(resignRecords);
        if (storeRecords.Count > 0) await _db.StoreReferences.AddRangeAsync(storeRecords);

        _db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = activeEmployeesFile.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = activeBytes, ContentType = GetContentType(activeEmployeesFile.FileName) });
        _db.UploadLogs.Add(new UploadLog { FileType = "resignations", FileName = resignationsFile.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = resignBytes, ContentType = GetContentType(resignationsFile.FileName) });
        _db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = storeReferenceFile.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = storeBytes, ContentType = GetContentType(storeReferenceFile.FileName) });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Runs after commit, outside the upload's own transaction, so a plan
        // creation/evaluation failure here can't roll back an otherwise-good upload.
        await _actionPlans.RunDetectionForPeriodAsync(month, year);

        var counts = new Dictionary<string, int>
        {
            ["active_employees"] = activeRecords.Count,
            ["resignations"] = resignRecords.Count,
            ["store_reference"] = storeRecords.Count,
        };

        var message = $"Uploaded {activeRecords.Count} active employees, {resignRecords.Count} resignations, and {storeRecords.Count} store references.";
        var unmatchedWarning = await BuildUnmatchedRoleEmailWarningAsync(storeRecords);
        if (unmatchedWarning != null) message += " " + unmatchedWarning;

        return (true, message, counts);
    }

    // Flags OM/OC/Head Manager emails in the just-uploaded store reference
    // batch that don't match any user account with the matching role, so the
    // admin can create or fix that account before the person complains their
    // stores are missing. Driven by IStoreAccessService's role map, grouped by
    // role, so a future role needs no change here.
    private async Task<string?> BuildUnmatchedRoleEmailWarningAsync(List<StoreReference> storeRecords)
    {
        var lines = new List<string>();

        foreach (var role in _storeAccess.RestrictedRoles)
        {
            var emails = storeRecords.Select(s => _storeAccess.GetEmailForRole(s, role))
                .Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (emails.Count == 0) continue;

            var roleUserEmails = (await _db.Users.Where(u => u.Role == role).Select(u => u.Email).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unmatched = emails.Where(e => !roleUserEmails.Contains(e)).OrderBy(e => e).ToList();
            if (unmatched.Count == 0) continue;

            var label = role.Replace("_", " ");
            lines.Add($"⚠ Missing {label}s: {unmatched.Count} email(s) don't match any account: {string.Join(", ", unmatched)}.");
        }

        return lines.Count == 0 ? null : string.Join(" ", lines);
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
            // Microsoft Forms exports use "Completion time" in EN and "وقت الانتهاء"
            // in AR; the export may also use "Start time" / "وقت البدء" as a fallback.
            var dateColCandidates = new[] {
                "Completion time", "وقت الانتهاء", "Start time", "وقت البدء",
                "Completion Time", "Start Time", "completion time", "start time"
            };
            int completedCol = 0;
            foreach (var candidate in dateColCandidates)
                if (headerMap.TryGetValue(NormalizeHeader(candidate), out completedCol)) break;

            if (completedCol > 0)
            {
                var cell = row.Cell(completedCol);
                if (!cell.IsEmpty())
                {
                    if (cell.DataType == XLDataType.DateTime) completed = cell.GetDateTime();
                    else if (DateTime.TryParse(cell.GetString(),
                             System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None, out var dt)) completed = dt;
                    else if (DateTime.TryParse(cell.GetString(), out var dt2)) completed = dt2;
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

    public async Task<(List<UploadHistoryItem> Items, int TotalCount)> GetHistoryPagedAsync(int page, int pageSize)
    {
        var logs = await _db.UploadLogs.OrderByDescending(l => l.UploadDate)
            .Select(l => new { l.Id, l.FileType, l.FileName, l.Month, l.Year, l.UploadDate, l.UploadedBy, HasFile = l.FileContent != null })
            .ToListAsync();

        var items = new List<UploadHistoryItem>();

        foreach (var group in logs.Where(l => PeriodFileTypes.Contains(l.FileType)).GroupBy(l => (l.Month, l.Year)))
        {
            items.Add(new UploadHistoryItem
            {
                Kind = "period",
                Month = group.Key.Month,
                Year = group.Key.Year,
                UploadDate = group.Max(l => l.UploadDate),
                UploadedBy = group.OrderByDescending(l => l.UploadDate).First().UploadedBy,
                PrimaryLogId = group.First().Id,
                Files = group.Select(l => new UploadFileRef { LogId = l.Id, FileType = l.FileType, FileName = l.FileName, HasFile = l.HasFile }).ToList(),
            });
        }

        foreach (var l in logs.Where(l => l.FileType == "exit_interviews"))
        {
            items.Add(new UploadHistoryItem
            {
                Kind = "exit_interviews",
                UploadDate = l.UploadDate,
                UploadedBy = l.UploadedBy,
                PrimaryLogId = l.Id,
                Files = new List<UploadFileRef> { new() { LogId = l.Id, FileType = l.FileType, FileName = l.FileName, HasFile = l.HasFile } },
            });
        }

        var ordered = items.OrderByDescending(i => i.UploadDate).ToList();
        var page_ = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (page_, ordered.Count);
    }

    public async Task<List<UploadHistoryItem>> GetAllHistoryAsync()
    {
        var (items, _) = await GetHistoryPagedAsync(1, int.MaxValue);
        return items;
    }

    public async Task<List<(byte[] Content, string ContentType, string FileName)>> GetGroupFilesAsync(int primaryLogId)
    {
        var pivot = await _db.UploadLogs.AsNoTracking()
            .Where(l => l.Id == primaryLogId)
            .Select(l => new { l.FileType, l.Month, l.Year })
            .FirstOrDefaultAsync();

        if (pivot == null) return new();

        List<UploadLog> logs;
        if (PeriodFileTypes.Contains(pivot.FileType))
            logs = await _db.UploadLogs.AsNoTracking()
                .Where(l => PeriodFileTypes.Contains(l.FileType) && l.Month == pivot.Month && l.Year == pivot.Year && l.FileContent != null)
                .ToListAsync();
        else
            logs = await _db.UploadLogs.AsNoTracking()
                .Where(l => l.Id == primaryLogId && l.FileContent != null)
                .ToListAsync();

        return logs
            .Select(l => (l.FileContent!, l.ContentType ?? "application/octet-stream", l.FileName))
            .ToList();
    }

    public async Task<List<(byte[] Content, string ContentType, string FileName)>> ExportGroupAsync(int primaryLogId)
    {
        var pivot = await _db.UploadLogs.AsNoTracking()
            .Where(l => l.Id == primaryLogId)
            .Select(l => new { l.FileType, l.Month, l.Year, l.FileName })
            .FirstOrDefaultAsync();

        if (pivot == null) return new();

        const string xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var result = new List<(byte[], string, string)>();

        if (PeriodFileTypes.Contains(pivot.FileType))
        {
            var active = await _db.ActiveEmployees.AsNoTracking()
                .Where(e => e.Month == pivot.Month && e.Year == pivot.Year).ToListAsync();
            var resigns = await _db.Resignations.AsNoTracking()
                .Where(r => r.Month == pivot.Month && r.Year == pivot.Year).ToListAsync();
            var stores = await _db.StoreReferences.AsNoTracking()
                .Where(s => s.Month == pivot.Month && s.Year == pivot.Year).ToListAsync();

            result.Add((BuildActiveEmployeesExcel(active), xlsx, $"Active_Employees_{pivot.Month}_{pivot.Year}.xlsx"));
            result.Add((BuildResignationsExcel(resigns), xlsx, $"Resignations_{pivot.Month}_{pivot.Year}.xlsx"));
            result.Add((BuildStoreReferenceExcel(stores), xlsx, $"Store_Reference_{pivot.Month}_{pivot.Year}.xlsx"));
        }
        else
        {
            var interviews = await _db.ExitInterviews.AsNoTracking()
                .Where(e => e.Month == pivot.Month && e.Year == pivot.Year).ToListAsync();
            result.Add((BuildExitInterviewsExcel(interviews), xlsx, $"Exit_Interviews_{pivot.Month}_{pivot.Year}.xlsx"));
        }

        return result;
    }

    private static byte[] BuildActiveEmployeesExcel(List<ActiveEmployee> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Active Employees");
        string[] headers = ["Employee ID", "Name", "Store", "Job Title", "Grade", "Payroll Group", "Cost Center", "Gender", "Hire Date"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        for (int r = 0; r < rows.Count; r++)
        {
            var e = rows[r]; int row = r + 2;
            ws.Cell(row, 1).Value = e.EmployeeId;
            ws.Cell(row, 2).Value = e.Name;
            ws.Cell(row, 3).Value = e.Store;
            ws.Cell(row, 4).Value = e.JobTitle;
            ws.Cell(row, 5).Value = e.Grade;
            ws.Cell(row, 6).Value = e.PayrollGroup;
            ws.Cell(row, 7).Value = e.CostCenter;
            ws.Cell(row, 8).Value = e.Gender;
            ws.Cell(row, 9).Value = e.HireDate.HasValue ? e.HireDate.Value.ToString("yyyy-MM-dd") : "";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    private static byte[] BuildResignationsExcel(List<Resignation> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Resignations");
        string[] headers = ["Employee ID", "Name", "Store", "Job Title", "Gender", "Hire Date", "Resignation Date", "Payroll Group", "Cost Center"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        for (int r = 0; r < rows.Count; r++)
        {
            var e = rows[r]; int row = r + 2;
            ws.Cell(row, 1).Value = e.EmployeeId;
            ws.Cell(row, 2).Value = e.Name;
            ws.Cell(row, 3).Value = e.Store;
            ws.Cell(row, 4).Value = e.JobTitle;
            ws.Cell(row, 5).Value = e.Gender;
            ws.Cell(row, 6).Value = e.HireDate.HasValue ? e.HireDate.Value.ToString("yyyy-MM-dd") : "";
            ws.Cell(row, 7).Value = e.ResignationDate.HasValue ? e.ResignationDate.Value.ToString("yyyy-MM-dd") : "";
            ws.Cell(row, 8).Value = e.PayrollGroup;
            ws.Cell(row, 9).Value = e.CostCenter;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    private static byte[] BuildStoreReferenceExcel(List<StoreReference> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Store Reference");
        string[] headers = ["Store Name", "Store Leader", "Operation Consultant", "Operation Manager", "Operation Consultant Email", "Operation Manager Email", "Head Manager", "Head Manager Email"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        for (int r = 0; r < rows.Count; r++)
        {
            var e = rows[r]; int row = r + 2;
            ws.Cell(row, 1).Value = e.StoreName;
            ws.Cell(row, 2).Value = e.StoreLeader;
            ws.Cell(row, 3).Value = e.OperationConsultant;
            ws.Cell(row, 4).Value = e.OperationManager;
            ws.Cell(row, 5).Value = e.OperationConsultantEmail;
            ws.Cell(row, 6).Value = e.OperationManagerEmail;
            ws.Cell(row, 7).Value = e.HeadManager;
            ws.Cell(row, 8).Value = e.HeadManagerEmail;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    private static byte[] BuildExitInterviewsExcel(List<ExitInterview> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Exit Interviews");
        string[] headers = ["Employee ID", "Store", "Store Leader", "Operation Consultant", "Operation Manager", "Job Title",
            "Reason For Leaving", "Would Return", "Overall Experience", "Workload Condition", "Fair Treatment",
            "Encourage Opinions", "Complaints Handling", "Benefits Match", "Teamwork", "Communication",
            "Task Fit", "Training", "Feedback", "Use Personal Abilities",
            "Reason Other Text", "Work Pressure Reason", "What Would Change", "What Learned", "Final Comments", "Submitted At"];
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        for (int r = 0; r < rows.Count; r++)
        {
            var e = rows[r]; int row = r + 2;
            ws.Cell(row, 1).Value = e.EmployeeId;
            ws.Cell(row, 2).Value = e.Store;
            ws.Cell(row, 3).Value = e.StoreLeader;
            ws.Cell(row, 4).Value = e.OperationConsultant;
            ws.Cell(row, 5).Value = e.OperationManager;
            ws.Cell(row, 6).Value = e.JobTitle;
            ws.Cell(row, 7).Value = e.ReasonForLeaving;
            ws.Cell(row, 8).Value = e.WouldReturn;
            ws.Cell(row, 9).Value = e.OverallExperience;
            ws.Cell(row, 10).Value = e.WorkloadCondition;
            ws.Cell(row, 11).Value = e.FairTreatment;
            ws.Cell(row, 12).Value = e.EncourageOpinions;
            ws.Cell(row, 13).Value = e.ComplaintsHandling;
            ws.Cell(row, 14).Value = e.BenefitsMatch;
            ws.Cell(row, 15).Value = e.Teamwork;
            ws.Cell(row, 16).Value = e.Communication;
            ws.Cell(row, 17).Value = e.TaskFit;
            ws.Cell(row, 18).Value = e.Training;
            ws.Cell(row, 19).Value = e.Feedback;
            ws.Cell(row, 20).Value = e.UsePersonalAbilities;
            ws.Cell(row, 21).Value = e.ReasonOtherText ?? "";
            ws.Cell(row, 22).Value = e.WorkPressureReasonText ?? "";
            ws.Cell(row, 23).Value = e.WhatWouldChangeText ?? "";
            ws.Cell(row, 24).Value = e.WhatLearnedText ?? "";
            ws.Cell(row, 25).Value = e.FinalCommentsText ?? "";
            ws.Cell(row, 26).Value = e.SubmittedAt.HasValue ? e.SubmittedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id)
    {
        var log = await _db.UploadLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log?.FileContent == null) return null;
        return (log.FileContent, log.ContentType ?? "application/octet-stream", log.FileName);
    }

    public async Task<(bool, string)> UpdateSingleFileAsync(
        string fileType, int month, int year, IFormFile file, string uploadedBy)
    {
        ValidateFile(file);
        var fileBytes = await ReadBytesAsync(file);

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Delete only the data rows and log entry for this specific file type
        switch (fileType)
        {
            case "active_employees":
                await _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year).ExecuteDeleteAsync();
                var activeRecords = ParseActiveEmployees(fileBytes, month, year);
                if (activeRecords.Count > 0) await _db.ActiveEmployees.AddRangeAsync(activeRecords);
                await _db.UploadLogs.Where(l => l.FileType == "active_employees" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                await _actionPlans.RunDetectionForPeriodAsync(month, year);
                return (true, $"Updated Active Employees for {new DateTime(year, month, 1):MMMM yyyy} — {activeRecords.Count} records.");

            case "resignations":
                await _db.Resignations.Where(r => r.Month == month && r.Year == year).ExecuteDeleteAsync();
                var resignRecords = ParseResignations(fileBytes, month, year);
                if (resignRecords.Count > 0) await _db.Resignations.AddRangeAsync(resignRecords);
                await _db.UploadLogs.Where(l => l.FileType == "resignations" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "resignations", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                await _actionPlans.RunDetectionForPeriodAsync(month, year);
                return (true, $"Updated Resignations for {new DateTime(year, month, 1):MMMM yyyy} — {resignRecords.Count} records.");

            case "store_reference":
                await _db.StoreReferences.Where(s => s.Month == month && s.Year == year).ExecuteDeleteAsync();
                var storeRecords = ParseStoreReference(fileBytes, month, year);
                // Throwing here rolls back the already-issued delete too (ambient
                // transaction, disposed without a commit) — the period's previous
                // store_reference data is left exactly as it was.
                ValidateNoDuplicateStoreReferences(storeRecords, month, year);
                if (storeRecords.Count > 0) await _db.StoreReferences.AddRangeAsync(storeRecords);
                await _db.UploadLogs.Where(l => l.FileType == "store_reference" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                await _actionPlans.RunDetectionForPeriodAsync(month, year);
                return (true, $"Updated Store Reference for {new DateTime(year, month, 1):MMMM yyyy} — {storeRecords.Count} records.");

            default:
                return (false, "Unknown file type.");
        }
    }

    public async Task DeleteLogAsync(int id)
    {
        var log = await _db.UploadLogs.FindAsync(id);
        if (log == null) return;

        if (PeriodFileTypes.Contains(log.FileType))
        {
            // The three period files are uploaded and validated together, so
            // deleting any one of them invalidates the whole month — remove
            // all three log entries and their underlying data together.
            await _db.ActiveEmployees.Where(e => e.Month == log.Month && e.Year == log.Year).ExecuteDeleteAsync();
            await _db.Resignations.Where(r => r.Month == log.Month && r.Year == log.Year).ExecuteDeleteAsync();
            await _db.StoreReferences.Where(s => s.Month == log.Month && s.Year == log.Year).ExecuteDeleteAsync();
            await _db.UploadLogs.Where(l => PeriodFileTypes.Contains(l.FileType) && l.Month == log.Month && l.Year == log.Year).ExecuteDeleteAsync();
            return;
        }

        // exit_interviews is intentionally excluded from period-cascading
        // deletion: each upload is a full, cumulative Forms export upserted
        // by response id, not a single month/year snapshot, so deleting one
        // log entry must not wipe data or other log rows.
        _db.UploadLogs.Remove(log);
        await _db.SaveChangesAsync();
    }
}
