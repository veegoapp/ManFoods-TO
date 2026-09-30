using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;

namespace MvcApp.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobTracker _jobTracker;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UploadService> _logger;
    private readonly IStringLocalizer<SharedResource> _L;

    private static readonly HashSet<string> PeriodFileTypes = new() { "active_employees", "resignations", "store_reference" };

    public UploadService(AppDbContext db, IStoreAccessService storeAccess, IServiceScopeFactory scopeFactory, IBackgroundJobTracker jobTracker, IMemoryCache cache, ILogger<UploadService> logger, IStringLocalizer<SharedResource> localizer)
    {
        _db = db;
        _storeAccess = storeAccess;
        _scopeFactory = scopeFactory;
        _jobTracker = jobTracker;
        _cache = cache;
        _logger = logger;
        _L = localizer;
    }

    // ActiveEmployees/Resignations changed — every cached full-table read derived
    // from them is now stale: Scorecard's historical records
    // (ScorecardService.LoadHistoricalRecordsAsync), 90-Day Turnover's active-hires/
    // resignation-tenures (NinetyDayTurnoverService), Retention's employee cohorts
    // (RetentionService), and Early Warning's historical records/resigned-ID list
    // (EarlyWarningService).
    private void InvalidateScorecardHistoricalCache()
    {
        _cache.Remove(ScorecardService.HistoricalRecordsCacheKey);
        _cache.Remove(NinetyDayTurnoverService.ActiveHiresCacheKey);
        _cache.Remove(NinetyDayTurnoverService.ResignationTenuresCacheKey);
        _cache.Remove(RetentionService.EmployeeCohortsCacheKey);
        _cache.Remove(EarlyWarningService.HistoricalRecordsCacheKey);
        _cache.Remove(EarlyWarningService.ResignedEmployeeIdsCacheKey);
    }

    // Runs detection in its own DI scope on a background task instead of on the
    // request thread: the request-scoped DbContext/services would be disposed as
    // soon as the HTTP response is sent, and detection loops over every store for
    // the period (slow for large datasets) — keeping it inline made uploads look
    // stuck/unresponsive and let a detection failure masquerade as an upload failure.
    // The job is registered with the tracker so the admin UI can poll its
    // running/succeeded/failed status.
    // Updating any one of the three period files (or re-uploading the whole
    // period) fires its own detection job for that same month/year — several
    // can easily overlap if files are updated in quick succession. Detection
    // is check-then-insert per store (EvaluateStoreAsync: read the active
    // plan, insert one if none exists), which is safe against a *second*
    // full run for the same period (it no-ops once a store's already marked
    // evaluated) but not against two runs racing on the same store's
    // check-then-insert at the same instant — that raced INSERT is exactly
    // what tripped ux_store_action_plans_active_store. Serializing runs per
    // period closes the race; the loser just re-checks already-current data
    // and finishes fast.
    private static readonly ConcurrentDictionary<(int Month, int Year), SemaphoreSlim> _detectionGates = new();

    private void FireAndForgetDetection(int month, int year, string label)
    {
        var jobId = _jobTracker.Start(label);
        _ = Task.Run(async () =>
        {
            var gate = _detectionGates.GetOrAdd((month, year), _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var actionPlans = scope.ServiceProvider.GetRequiredService<IStoreActionPlanService>();
                await actionPlans.RunDetectionForPeriodAsync(month, year);
                _jobTracker.Succeed(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action plan detection failed for period {Month}/{Year}", month, year);
                _jobTracker.Fail(jobId, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        });
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

    // .xlsx is a ZIP archive ("PK\x03\x04" etc.); legacy .xls is an OLE2
    // Compound File ("\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1"). Checking these
    // magic bytes catches a file that's merely been renamed to look like
    // Excel before it ever reaches ClosedXML — defense in depth on top of
    // the extension check, not a replacement for it.
    private static readonly byte[][] ZipSignatures =
    {
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        new byte[] { 0x50, 0x4B, 0x05, 0x06 },
        new byte[] { 0x50, 0x4B, 0x07, 0x08 },
    };
    private static readonly byte[] Ole2Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    private async Task ValidateFileAsync(IFormFile file)
    {
        const long maxBytes = 10 * 1024 * 1024; // 10 MB
        if (file.Length > maxBytes)
            throw new InvalidOperationException(_L["Msg_FileTooLarge"].Value);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
            throw new InvalidOperationException(_L["Msg_OnlyExcelFiles"].Value);

        var header = new byte[8];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAsync(header.AsMemory(0, 8));
            if (read < 4 ||
                !(ZipSignatures.Any(sig => header.AsSpan(0, 4).SequenceEqual(sig)) ||
                  (read == 8 && header.AsSpan(0, 8).SequenceEqual(Ole2Signature))))
            {
                throw new InvalidOperationException(_L["Msg_OnlyExcelFiles"].Value);
            }
        }
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

    /// <summary>Row-quality issues found while parsing — a row is still imported
    /// (a blank Store or unreadable date isn't a reason to drop real headcount/
    /// resignation data), but these counts let the upload result tell the admin
    /// what to go fix instead of those rows just silently vanishing from every
    /// store-scoped report.</summary>
    private record ParseIssues(int MissingStore, int MissingOrInvalidHireDate, int MissingOrInvalidResignationDate = 0);

    private static (List<ActiveEmployee> Records, ParseIssues Issues) ParseActiveEmployees(byte[] fileBytes, int month, int year)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        var records = new List<ActiveEmployee>();
        int missingStore = 0, badHireDate = 0;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID", "id");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            var store = Col(row, ws, "Store", "store");
            var hireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date");
            if (string.IsNullOrWhiteSpace(store)) missingStore++;
            if (hireDate == null) badHireDate++;

            records.Add(new ActiveEmployee
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = store,
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Grade = Col(row, ws, "Grade", "grade"),
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = hireDate,
            });
        }
        return (records, new ParseIssues(missingStore, badHireDate));
    }

    private static (List<Resignation> Records, ParseIssues Issues) ParseResignations(byte[] fileBytes, int month, int year)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        var records = new List<Resignation>();
        int missingStore = 0, badHireDate = 0, badResignDate = 0;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var empId = Col(row, ws, "Employee ID", "EmployeeID", "employee_id", "ID");
            var name = Col(row, ws, "Name", "Employee Name", "name");
            if (string.IsNullOrEmpty(empId) && string.IsNullOrEmpty(name)) continue;

            var store = Col(row, ws, "Store", "store");
            var hireDate = ColDate(row, ws, "Hire Date", "HireDate", "hire_date", "Join Date");
            var resignDate = ColDate(row, ws, "Resignation Date", "ResignationDate", "resignation_date", "Last Day");
            if (string.IsNullOrWhiteSpace(store)) missingStore++;
            if (hireDate == null) badHireDate++;
            if (resignDate == null) badResignDate++;

            records.Add(new Resignation
            {
                Month = month, Year = year,
                EmployeeId = empId, Name = name,
                Store = store,
                JobTitle = Col(row, ws, "Job Title", "JobTitle", "Position", "job_title"),
                Gender = Col(row, ws, "Gender", "gender"),
                HireDate = hireDate,
                ResignationDate = resignDate,
                PayrollGroup = Col(row, ws, "Payroll Group", "PayrollGroup", "payroll_group"),
                CostCenter = Col(row, ws, "Cost Center", "CostCenter", "cost_center"),
            });
        }
        return (records, new ParseIssues(missingStore, badHireDate, badResignDate));
    }

    private string? DescribeIssues(string sectionLabel, ParseIssues issues)
    {
        var parts = new List<string>();
        if (issues.MissingStore > 0) parts.Add(string.Format(_L["Msg_IssueMissingStore"].Value, issues.MissingStore));
        if (issues.MissingOrInvalidHireDate > 0) parts.Add(string.Format(_L["Msg_IssueBadHireDate"].Value, issues.MissingOrInvalidHireDate));
        if (issues.MissingOrInvalidResignationDate > 0) parts.Add(string.Format(_L["Msg_IssueBadResignDate"].Value, issues.MissingOrInvalidResignationDate));
        return parts.Count == 0 ? null : string.Format(_L["Msg_IssuesSummary"].Value, sectionLabel, string.Join(", ", parts));
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
                SeniorOperationConsultant = Col(row, ws, "Senior Operation Consultant", "SeniorOperationConsultant", "Senior OC"),
                SeniorOperationConsultantEmail = Col(row, ws, "Senior Operation Consultant Email", "SeniorOperationConsultantEmail", "Senior OC Email").Trim().ToLowerInvariant(),
                OperationDirector = Col(row, ws, "Operation Director", "OperationDirector", "OD"),
                OperationDirectorEmail = Col(row, ws, "Operation Director Email", "OperationDirectorEmail", "OD Email").Trim().ToLowerInvariant(),
            });
        }
        return records;
    }

    // Guards the one authorization-critical invariant of this table: a store
    // can only have ONE set of role emails (OM/OC/Head Manager/Senior OC/
    // Operation Director) per (Month, Year), because StoreAccessService grants
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

    public async Task<(bool, string, Dictionary<string, int>, string?)> UploadPeriodDataAsync(
        IFormFile activeEmployeesFile, IFormFile resignationsFile, IFormFile storeReferenceFile,
        int month, int year, string uploadedBy)
    {
        await ValidateFileAsync(activeEmployeesFile);
        await ValidateFileAsync(resignationsFile);
        await ValidateFileAsync(storeReferenceFile);

        var activeBytes = await ReadBytesAsync(activeEmployeesFile);
        var resignBytes = await ReadBytesAsync(resignationsFile);
        var storeBytes = await ReadBytesAsync(storeReferenceFile);

        // Parsed before the transaction opens — a bad workbook throws here and
        // nothing has touched the database, so partial uploads are impossible.
        var (activeRecords, activeIssues) = ParseActiveEmployees(activeBytes, month, year);
        var (resignRecords, resignIssues) = ParseResignations(resignBytes, month, year);
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
        InvalidateScorecardHistoricalCache();

        FireAndForgetDetection(month, year, string.Format(_L["Msg_JobMonthlyData"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy")));

        var counts = new Dictionary<string, int>
        {
            ["active_employees"] = activeRecords.Count,
            ["resignations"] = resignRecords.Count,
            ["store_reference"] = storeRecords.Count,
        };

        var message = string.Format(_L["Msg_UploadPeriodSuccess"].Value, activeRecords.Count, resignRecords.Count, storeRecords.Count);

        var warningLines = new[]
        {
            DescribeIssues(_L["UL_ActiveEmployees"].Value, activeIssues),
            DescribeIssues(_L["Common_Resignations"].Value, resignIssues),
            await BuildUnmatchedRoleEmailWarningAsync(storeRecords),
        }.Where(w => w != null).ToList();
        var warning = warningLines.Count == 0 ? null : string.Join("\n", warningLines);
        if (warning != null) _logger.LogWarning("{Warning}", warning);

        return (true, message, counts, warning);
    }

    public async Task<List<(int Month, int Year)>> GetExistingPeriodKeysAsync() =>
        (await _db.UploadLogs.Where(l => PeriodFileTypes.Contains(l.FileType))
            .Select(l => new { l.Month, l.Year }).Distinct().ToListAsync())
            .Select(x => (x.Month, x.Year)).ToList();

    // Flags OM/OC/Head Manager emails in the just-uploaded store reference
    // batch that don't match any user account with the matching role, so the
    // admin can create or fix that account before the person complains their
    // stores are missing. Driven by IStoreAccessService's role map, grouped by
    // role, so a future role needs no change here. Surfaced as a warning banner
    // on the Uploads page (see UploadPeriodDataAsync/UpdateSingleFileAsync).
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
            lines.Add(string.Format(_L["Msg_UnmatchedRoleEmails"].Value, label, unmatched.Count, string.Join(", ", unmatched)));
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string NormalizeHeader(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public async Task<(bool, string, int)> UploadExitInterviewsAsync(IFormFile file, string uploadedBy)
    {
        await ValidateFileAsync(file);
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

        var parsed = new List<(string ResponseId, string EmployeeId, DateTime? Completed, ExitInterview Row)>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var responseId = Get(row, "ID");
            var employeeId = Get(row, "الرقم الوظيفى");
            if (string.IsNullOrWhiteSpace(responseId) && string.IsNullOrWhiteSpace(employeeId)) continue;

            // The current template has no Forms "ID" column (it was trimmed out),
            // so fall back to a synthetic key built from row content that will be
            // identical across re-uploads of the same response but distinct across
            // different ones — Upsert-by-response-id still works without a real ID.
            if (string.IsNullOrWhiteSpace(responseId))
                responseId = $"{employeeId}|{Get(row, "المطعم")}|{Get(row, "Start time")}|{Get(row, "Completion time")}";

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
                // Store and Job Title now come straight from their own columns in
                // the export instead of being resolved by matching the employee ID
                // against a Resignation record — that match silently failed (leaving
                // Store/StoreLeader/OC/OM/JobTitle blank, with no warning) whenever
                // the ID was mistyped or the resignation hadn't been uploaded yet.
                Store = Get(row, "المطعم"),
                JobTitle = Get(row, "الوظيفة"),
                SubmittedAt = completed,
                // Month/Year are set below, after the resignation-date lookup
                // (Resignation date -> Completion time -> Start time priority).

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

            parsed.Add((responseId, employeeId, completed, interview));
        }

        // Job Title comes from the employee's most recent resignation record
        // (best-effort only — a miss here just leaves JobTitle blank, it no longer
        // affects Store/StoreLeader/OC/OM at all).
        var employeeIds = parsed.Where(p => !string.IsNullOrWhiteSpace(p.EmployeeId)).Select(p => p.EmployeeId).Distinct().ToList();
        var resignations = employeeIds.Count == 0
            ? new List<Resignation>()
            : await _db.Resignations.Where(r => employeeIds.Contains(r.EmployeeId)).ToListAsync();
        var resignationByEmployee = resignations
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).First());

        // Store Leader / OC / OM are resolved from the Store column itself — the
        // store reference closest to (or, failing that, the latest known as of)
        // the interview's own resolved period.
        var storeNames = parsed.Where(p => !string.IsNullOrWhiteSpace(p.Row.Store)).Select(p => p.Row.Store).Distinct().ToList();
        var storeRefs = storeNames.Count == 0
            ? new List<StoreReference>()
            : await _db.StoreReferences.Where(s => storeNames.Contains(s.StoreName)).ToListAsync();
        // Tracks rows whose "المطعم" value has no matching Store Reference —
        // those rows keep Store but end up with a blank Store Leader/OC/OM,
        // usually because the two files spell the same store differently.
        var unmatchedStoreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, employeeId, completed, interview) in parsed)
        {
            // Month/Year priority: the employee's actual resignation date (most
            // accurate) -> the form's Completion time -> its Start time. Falls
            // back to 0/0 (the "undated" sentinel) only when none of the three
            // is available.
            Resignation? res = null;
            if (!string.IsNullOrWhiteSpace(employeeId) && resignationByEmployee.TryGetValue(employeeId, out var r))
            {
                res = r;
                // The "الوظيفة" column is the primary source (set at parse time);
                // this is only a fallback for rows where that column was left blank.
                if (string.IsNullOrWhiteSpace(interview.JobTitle)) interview.JobTitle = r.JobTitle;
            }

            if (res?.ResignationDate is { } resignDate)
            {
                interview.Month = resignDate.Month;
                interview.Year = resignDate.Year;
            }
            else if (completed.HasValue)
            {
                interview.Month = completed.Value.Month;
                interview.Year = completed.Value.Year;
            }

            if (string.IsNullOrWhiteSpace(interview.Store)) continue;

            var refMatch = storeRefs.FirstOrDefault(s => s.StoreName == interview.Store && s.Year == interview.Year && s.Month == interview.Month)
                ?? storeRefs.Where(s => s.StoreName == interview.Store).OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).FirstOrDefault();
            if (refMatch != null)
            {
                interview.StoreLeader = refMatch.StoreLeader;
                interview.OperationConsultant = refMatch.OperationConsultant;
                interview.OperationManager = refMatch.OperationManager;
            }
            else
            {
                unmatchedStoreNames.Add(interview.Store);
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

        var missingStore = parsed.Count(p => string.IsNullOrWhiteSpace(p.Row.Store));
        var message = missingStore > 0
            ? string.Format(_L["Msg_ExitProcessedWithMissingStore"].Value, parsed.Count, missingStore)
            : string.Format(_L["Msg_ExitProcessed"].Value, parsed.Count);
        if (unmatchedStoreNames.Count > 0)
            message += " " + string.Format(_L["Msg_ExitUnmatchedStores"].Value, unmatchedStoreNames.Count, string.Join(", ", unmatchedStoreNames.OrderBy(s => s)));
        return (true, message, parsed.Count);
    }

    public async Task<(List<UploadHistoryItem> Items, int TotalCount)> GetHistoryPagedAsync(int page, int pageSize, string sort = "date", string dir = "desc")
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

        bool asc = dir == "asc";
        IOrderedEnumerable<UploadHistoryItem> sorted = sort switch
        {
            "type" => asc ? items.OrderBy(i => i.Kind) : items.OrderByDescending(i => i.Kind),
            "name" => asc
                ? items.OrderBy(i => i.Files.Select(f => f.FileName).OrderBy(n => n).FirstOrDefault())
                : items.OrderByDescending(i => i.Files.Select(f => f.FileName).OrderBy(n => n).FirstOrDefault()),
            "period" => asc
                ? items.OrderBy(i => i.Year ?? 0).ThenBy(i => i.Month ?? 0)
                : items.OrderByDescending(i => i.Year ?? 0).ThenByDescending(i => i.Month ?? 0),
            "uploadedby" => asc ? items.OrderBy(i => i.UploadedBy) : items.OrderByDescending(i => i.UploadedBy),
            _ => asc ? items.OrderBy(i => i.UploadDate) : items.OrderByDescending(i => i.UploadDate),
        };

        var ordered = sorted.ToList();
        var page_ = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return (page_, ordered.Count);
    }

    public async Task<List<UploadHistoryItem>> GetAllHistoryAsync()
    {
        var (items, _) = await GetHistoryPagedAsync(1, int.MaxValue);
        return items;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> GetFileAsync(int id)
    {
        var log = await _db.UploadLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log?.FileContent == null) return null;
        return (log.FileContent, log.ContentType ?? "application/octet-stream", log.FileName);
    }

    public async Task<UploadFilePreview?> PreviewFileAsync(int logId, int maxRows = 300)
    {
        var log = await _db.UploadLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logId);
        if (log?.FileContent == null) return null;

        using var ms = new MemoryStream(log.FileContent);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);
        var usedRange = ws.RangeUsed();
        if (usedRange == null) return new UploadFilePreview { FileName = log.FileName };

        var headerRow = usedRange.FirstRow();
        var headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
        var colCount = headers.Count;

        var dataRows = usedRange.RowsUsed().Skip(1).ToList();
        var rows = dataRows.Take(maxRows)
            .Select(r => Enumerable.Range(1, colCount).Select(c => r.Cell(c).GetFormattedString()).ToList())
            .ToList();

        return new UploadFilePreview
        {
            FileName = log.FileName,
            Headers = headers,
            Rows = rows,
            TotalRows = dataRows.Count,
        };
    }

    public async Task<(bool, string, string?)> UpdateSingleFileAsync(
        string fileType, int month, int year, IFormFile file, string uploadedBy)
    {
        await ValidateFileAsync(file);
        var fileBytes = await ReadBytesAsync(file);

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Delete only the data rows and log entry for this specific file type
        switch (fileType)
        {
            case "active_employees":
                await _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year).ExecuteDeleteAsync();
                var (activeRecords, activeIssues) = ParseActiveEmployees(fileBytes, month, year);
                if (activeRecords.Count > 0) await _db.ActiveEmployees.AddRangeAsync(activeRecords);
                await _db.UploadLogs.Where(l => l.FileType == "active_employees" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                InvalidateScorecardHistoricalCache();
                FireAndForgetDetection(month, year, string.Format(_L["Msg_JobActiveEmployees"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy")));
                return (true, string.Format(_L["Msg_UpdatedActiveEmployees"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy"), activeRecords.Count), DescribeIssues(_L["UL_ActiveEmployees"].Value, activeIssues));

            case "resignations":
                await _db.Resignations.Where(r => r.Month == month && r.Year == year).ExecuteDeleteAsync();
                var (resignRecords, resignIssues) = ParseResignations(fileBytes, month, year);
                if (resignRecords.Count > 0) await _db.Resignations.AddRangeAsync(resignRecords);
                await _db.UploadLogs.Where(l => l.FileType == "resignations" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "resignations", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                InvalidateScorecardHistoricalCache();
                FireAndForgetDetection(month, year, string.Format(_L["Msg_JobResignations"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy")));
                return (true, string.Format(_L["Msg_UpdatedResignations"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy"), resignRecords.Count), DescribeIssues(_L["Common_Resignations"].Value, resignIssues));

            case "store_reference":
                await _db.StoreReferences.Where(s => s.Month == month && s.Year == year).ExecuteDeleteAsync();
                var storeRecords = ParseStoreReference(fileBytes, month, year);
                ValidateNoDuplicateStoreReferences(storeRecords, month, year);
                if (storeRecords.Count > 0) await _db.StoreReferences.AddRangeAsync(storeRecords);
                await _db.UploadLogs.Where(l => l.FileType == "store_reference" && l.Month == month && l.Year == year).ExecuteDeleteAsync();
                _db.UploadLogs.Add(new UploadLog { FileType = "store_reference", FileName = file.FileName, Month = month, Year = year, UploadedBy = uploadedBy, FileContent = fileBytes, ContentType = GetContentType(file.FileName) });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                FireAndForgetDetection(month, year, string.Format(_L["Msg_JobStoreReference"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy")));
                var storeWarning = await BuildUnmatchedRoleEmailWarningAsync(storeRecords);
                return (true, string.Format(_L["Msg_UpdatedStoreReference"].Value, new DateTime(year, month, 1).ToString("MMMM yyyy"), storeRecords.Count), storeWarning);

            default:
                return (false, _L["Msg_UnknownFileType"].Value, null);
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
            InvalidateScorecardHistoricalCache();
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
