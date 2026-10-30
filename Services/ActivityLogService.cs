using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;

namespace MvcApp.Services;

/// <summary>
/// Reads and clears the unified activity_logs table behind the Super Admin "Activity Logs" page.
///
/// Database load: filters, ordering and paging all run in SQL — a view is one grouped COUNT, one page of 25 rows
/// (OFFSET/FETCH over the occurred_at index) and, when the page has admin actions, one lookup of the target users'
/// emails. Nothing is loaded and then filtered in the application.
/// </summary>
public class ActivityLogService : IActivityLogService
{
    public const int PageSize = 25;
    /// <summary>The deepest row offset a request may reach; beyond it the page asks to narrow the filters.</summary>
    public const int MaxDepth = 10_000;
    private const int MaxSearchLength = 100;
    /// <summary>Most rows one Excel export carries (same ceiling as the page's paging depth).</summary>
    public const int MaxExportRows = MaxDepth;
    private const string SecurityOverviewCacheKey = "activity-logs:security-overview";

    // Suspicious-pattern thresholds (last 24 hours unless noted).
    private const int FailedPerIpThreshold = 5;
    private const int FailedPerAccountThreshold = 5;
    private const int DeniedPerUserThreshold = 3;
    private const int KnownIpLookbackDays = 90;
    private const int MaxAlerts = 10;

    /// <summary>Filter key → the stored action codes it selects.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ActionFilters = new Dictionary<string, string[]>
    {
        ["login"] = new[] { ActivityActions.LoginSuccess },
        ["failed-login"] = new[] { ActivityActions.LoginFailed },
        ["unknown-login"] = new[] { ActivityActions.LoginUnknown },
        ["blocked-login"] = new[] { ActivityActions.LoginBlocked },
        ["logout"] = new[] { ActivityActions.Logout },
        ["upload"] = new[] { ActivityActions.Upload, ActivityActions.UploadLogDelete },
        ["report-download"] = new[] { ActivityActions.ReportDownload },
        ["action-plan"] = new[] { ActivityActions.ActionPlanNote, ActivityActions.ActionPlanRecommendation, ActivityActions.ActionPlanAssign, ActivityActions.ActionPlanClose },
        ["create-user"] = new[] { ActivityActions.UserCreate, ActivityActions.UserBulkUpload },
        ["edit-user"] = new[] { ActivityActions.UserUpdate },
        ["delete-user"] = new[] { ActivityActions.UserDelete },
        ["role-change"] = new[] { ActivityActions.RoleChange },
        ["password"] = new[] { ActivityActions.PasswordGenerate, ActivityActions.PasswordGenerateBulk },
        ["otp"] = new[] { ActivityActions.OtpGenerate, ActivityActions.OtpGenerateAdmin },
        ["recovery-key"] = new[] { ActivityActions.RecoveryKeyRegenerate, ActivityActions.RecoveryKeyUse },
        ["settings"] = new[] { ActivityActions.SettingsAccessPolicy, ActivityActions.SettingsPageVisibility },
        ["clear-history"] = new[] { ActivityActions.ActivityLogsClear },
        ["denied"] = new[] { ActivityActions.AccessDenied },
    };

    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _L;
    private readonly IMemoryCache _cache;

    public ActivityLogService(AppDbContext db, IStringLocalizer<SharedResource> localizer, IMemoryCache cache)
    {
        _db = db;
        _L = localizer;
        _cache = cache;
    }

    public async Task<bool> VerifyPasswordAsync(int userId, string password)
    {
        var hash = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PasswordHash).FirstOrDefaultAsync();
        return !string.IsNullOrEmpty(hash) && BCrypt.Net.BCrypt.Verify(password, hash);
    }

    // ── reading ──────────────────────────────────────────────────────────────

    public async Task<ActivityLogPage> GetAsync(ActivityLogQuery query)
    {
        try { return await ReadAsync(query); }
        catch (System.Data.Common.DbException)
        {
            // Almost always "Invalid object name 'activity_logs'": the migration has not been applied yet.
            return new ActivityLogPage { Unavailable = true, Page = 1, PageSize = PageSize, TotalPages = 1 };
        }
    }

    public async Task<ActivitySecurityOverview> GetSecurityOverviewAsync()
    {
        if (_cache.TryGetValue(SecurityOverviewCacheKey, out ActivitySecurityOverview? cached) && cached != null) return cached;

        var overview = new ActivitySecurityOverview();
        try
        {
            var now = DateTime.UtcNow;
            var since24h = now.AddHours(-24);
            var since7d = now.AddDays(-7);

            // Two small queries over the occurred_at index, once a minute at most (not per filter change).
            var counts = await _db.ActivityLogs.AsNoTracking()
                .Where(l => l.OccurredAt >= since24h && l.Category == ActivityCategories.Login)
                .GroupBy(l => l.Action)
                .Select(g => new { Action = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var c in counts)
            {
                if (c.Action == ActivityActions.LoginFailed || c.Action == ActivityActions.LoginUnknown) overview.FailedLogins24h += c.Count;
                else if (c.Action == ActivityActions.LoginBlocked) overview.BlockedLogins24h += c.Count;
            }

            overview.ActiveUsers7d = await _db.ActivityLogs.AsNoTracking()
                .Where(l => l.OccurredAt >= since7d && l.Action == ActivityActions.LoginSuccess && l.UserEmail != null)
                .Select(l => l.UserEmail!.ToLower())
                .Distinct()
                .CountAsync();

            overview.Alerts = await FindAlertsAsync(since24h);
        }
        catch (System.Data.Common.DbException)
        {
            return overview; // table not migrated yet — the page shows its "unavailable" notice; do not cache zeros
        }

        _cache.Set(SecurityOverviewCacheKey, overview, TimeSpan.FromMinutes(1));
        return overview;
    }

    // Rule-based detection over the last 24 hours. Every query is bounded by occurred_at and returns only
    // groups over a threshold (or a short distinct list), so the cost does not grow with the table.
    private async Task<List<ActivityAlert>> FindAlertsAsync(DateTime since24h)
    {
        var alerts = new List<(int Rank, int Count, ActivityAlert Alert)>();
        string Seen(DateTime t) => CairoTime.Format(t);
        string Msg(string key, params object[] args) => string.Format(_L[key].Value, args);

        // 1) many failed attempts from one IP address
        var byIp = await _db.ActivityLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= since24h && l.IpAddress != null
                        && (l.Action == ActivityActions.LoginFailed || l.Action == ActivityActions.LoginUnknown))
            .GroupBy(l => l.IpAddress!)
            .Select(g => new { Ip = g.Key, Count = g.Count(), Last = g.Max(x => x.OccurredAt) })
            .Where(x => x.Count >= FailedPerIpThreshold)
            .OrderByDescending(x => x.Count).Take(MaxAlerts)
            .ToListAsync();
        foreach (var x in byIp)
            alerts.Add((0, x.Count, new ActivityAlert
            {
                Kind = "brute-force", Severity = "high", Message = Msg("ActLog_Alert_BruteForce", x.Count, x.Ip),
                LastSeen = Seen(x.Last), Status = "failed", Search = x.Ip,
            }));

        // 2) many failed attempts against one account
        var byAccount = await _db.ActivityLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= since24h && l.UserEmail != null
                        && (l.Action == ActivityActions.LoginFailed || l.Action == ActivityActions.LoginBlocked))
            .GroupBy(l => l.UserEmail!.ToLower())
            .Select(g => new { Email = g.Key, Count = g.Count(), Last = g.Max(x => x.OccurredAt) })
            .Where(x => x.Count >= FailedPerAccountThreshold)
            .OrderByDescending(x => x.Count).Take(MaxAlerts)
            .ToListAsync();
        foreach (var x in byAccount)
            alerts.Add((1, x.Count, new ActivityAlert
            {
                Kind = "targeted-account", Severity = "high", Message = Msg("ActLog_Alert_TargetedAccount", x.Count, x.Email),
                LastSeen = Seen(x.Last), Status = "failed", Search = x.Email,
            }));

        // 3) the same person repeatedly refused an admin action
        var denied = await _db.ActivityLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= since24h && l.UserEmail != null && l.Action == ActivityActions.AccessDenied)
            .GroupBy(l => l.UserEmail!.ToLower())
            .Select(g => new { Email = g.Key, Count = g.Count(), Last = g.Max(x => x.OccurredAt) })
            .Where(x => x.Count >= DeniedPerUserThreshold)
            .OrderByDescending(x => x.Count).Take(MaxAlerts)
            .ToListAsync();
        foreach (var x in denied)
            alerts.Add((3, x.Count, new ActivityAlert
            {
                Kind = "repeated-denied", Severity = "medium", Message = Msg("ActLog_Alert_RepeatedDenied", x.Count, x.Email),
                LastSeen = Seen(x.Last), Action = "denied", Search = x.Email,
            }));

        // 4) an admin portal sign-in from an IP that account has never used before
        var recent = await _db.ActivityLogs.AsNoTracking()
            .Where(l => l.OccurredAt >= since24h && l.Action == ActivityActions.LoginSuccess && l.Portal == "Admin"
                        && l.UserEmail != null && l.IpAddress != null)
            .Select(l => new { Email = l.UserEmail!.ToLower(), Ip = l.IpAddress!, At = l.OccurredAt })
            .Take(500)
            .ToListAsync();
        if (recent.Count > 0)
        {
            var emails = recent.Select(r => r.Email).Distinct().ToList();
            var lookback = DateTime.UtcNow.AddDays(-KnownIpLookbackDays);
            var known = await _db.ActivityLogs.AsNoTracking()
                .Where(l => l.OccurredAt >= lookback && l.OccurredAt < since24h && l.Action == ActivityActions.LoginSuccess
                            && l.UserEmail != null && l.IpAddress != null && emails.Contains(l.UserEmail.ToLower()))
                .Select(l => new { Email = l.UserEmail!.ToLower(), Ip = l.IpAddress! })
                .Distinct()
                .ToListAsync();
            var knownByEmail = known.GroupBy(k => k.Email).ToDictionary(g => g.Key, g => g.Select(k => k.Ip).ToHashSet());
            foreach (var g in recent.GroupBy(r => (r.Email, r.Ip)))
            {
                // No earlier history for the account = a first sign-in, not a "new" IP.
                if (!knownByEmail.TryGetValue(g.Key.Email, out var ips) || ips.Contains(g.Key.Ip)) continue;
                alerts.Add((2, g.Count(), new ActivityAlert
                {
                    Kind = "new-admin-ip", Severity = "medium", Message = Msg("ActLog_Alert_NewAdminIp", g.Key.Email, g.Key.Ip),
                    LastSeen = Seen(g.Max(r => r.At)), Action = "login", Search = g.Key.Email,
                }));
            }
        }

        return alerts.OrderBy(a => a.Rank).ThenByDescending(a => a.Count).Take(MaxAlerts).Select(a => a.Alert).ToList();
    }

    public async Task<byte[]> ExportAsync(ActivityLogQuery query)
    {
        var rows = await Filtered(query, includeStatus: true)
            .OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id)
            .Take(MaxExportRows)
            .ToListAsync();

        var targetIds = rows.Where(r => r.TargetUserId != null).Select(r => r.TargetUserId!.Value).Distinct().ToList();
        var targets = targetIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().Where(u => targetIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Activity Logs");
        string[] heads =
        {
            _L["ActLog_ColDateTime"].Value, _L["ActLog_ColUser"].Value, _L["ActLog_ColAction"].Value, _L["ActLog_ColResult"].Value,
            _L["ActLog_ColTarget"].Value, _L["Users_IpAddress"].Value, _L["ActLog_ColDescription"].Value,
        };
        for (var c = 0; c < heads.Length; c++) ws.Cell(1, c + 1).Value = heads[c];
        ws.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < rows.Count; i++)
        {
            var item = ToItem(rows[i], targets);
            ws.Cell(i + 2, 1).Value = item.TimeDisplay;
            ws.Cell(i + 2, 2).Value = item.User;
            ws.Cell(i + 2, 3).Value = item.ActionLabel;
            ws.Cell(i + 2, 4).Value = item.ResultLabel;
            ws.Cell(i + 2, 5).Value = item.Target ?? "";
            ws.Cell(i + 2, 6).Value = item.Ip ?? "";
            ws.Cell(i + 2, 7).Value = item.Description;
        }
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        ws.SheetView.FreezeRows(1);

        ExcelExportGuard.Neutralize(wb);   // emails/details are user-controlled text (see ExcelExportGuard)
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private IQueryable<ActivityLog> Filtered(ActivityLogQuery q, bool includeStatus)
    {
        IQueryable<ActivityLog> logs = _db.ActivityLogs.AsNoTracking();
        if (q.FromDay != null) { var from = CairoTime.DayStartUtc(q.FromDay.Value); logs = logs.Where(l => l.OccurredAt >= from); }
        if (q.ToDay != null) { var to = CairoTime.DayEndUtc(q.ToDay.Value); logs = logs.Where(l => l.OccurredAt <= to); }
        var search = (q.Search ?? "").Trim();
        if (search.Length > MaxSearchLength) search = search[..MaxSearchLength];
        if (search != "")
        {
            var needle = search.ToLower();
            // The "User" box also finds an IP address, so an alert about an IP can open its rows.
            logs = logs.Where(l => (l.UserEmail != null && l.UserEmail.ToLower().Contains(needle))
                                || (l.IpAddress != null && l.IpAddress.Contains(needle)));
        }
        if (q.Action != null && ActionFilters.TryGetValue(q.Action, out var codes)) logs = logs.Where(l => codes.Contains(l.Action));
        if (includeStatus)
        {
            if (q.Status == "success") logs = logs.Where(l => l.Success);
            else if (q.Status == "failed") logs = logs.Where(l => !l.Success);
        }
        return logs;
    }

    private async Task<ActivityLogPage> ReadAsync(ActivityLogQuery q)
    {
        var page = Math.Max(1, q.Page);

        // summary: one grouped query (ignores the Result filter so the cards stay meaningful)
        var groups = await Filtered(q, includeStatus: false)
            .GroupBy(l => new { l.Success, Admin = l.Category != ActivityCategories.Login })
            .Select(g => new { g.Key.Success, g.Key.Admin, Count = g.Count() })
            .ToListAsync();
        var summary = new ActivityLogSummary();
        foreach (var g in groups)
        {
            if (g.Success) summary.Successful += g.Count; else summary.Failed += g.Count;
            if (g.Admin) summary.AdminActions += g.Count;
        }
        summary.Total = summary.Successful + summary.Failed;

        var total = q.Status switch { "success" => summary.Successful, "failed" => summary.Failed, _ => summary.Total };
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var capped = (long)(page - 1) * PageSize >= MaxDepth;
        if (capped) page = MaxDepth / PageSize;
        page = Math.Min(page, totalPages);

        var rows = await Filtered(q, includeStatus: true)
            .OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync();

        var targetIds = rows.Where(r => r.TargetUserId != null).Select(r => r.TargetUserId!.Value).Distinct().ToList();
        var targets = targetIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().Where(u => targetIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

        return new ActivityLogPage
        {
            Items = rows.Select(r => ToItem(r, targets)).ToList(),
            Summary = summary, Page = page, PageSize = PageSize,
            TotalItems = total, TotalPages = totalPages, Capped = capped,
        };
    }

    // ── row → item ───────────────────────────────────────────────────────────

    private static string Key(string action) => action.Replace('.', '_').Replace('-', '_');

    private string ActionLabel(string action)
    {
        var s = _L["ActLog_Act_" + Key(action)];
        return s.ResourceNotFound ? action : s.Value;
    }

    private string? ReasonLabel(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return null;
        var s = _L["ActLog_Reason_" + Key(reason)];
        return s.ResourceNotFound ? reason : s.Value;
    }

    private ActivityLogItem ToItem(ActivityLog r, IReadOnlyDictionary<int, string> targets)
    {
        string? target = r.TargetUserId == null ? null : targets.TryGetValue(r.TargetUserId.Value, out var email) ? email : $"#{r.TargetUserId}";
        var reason = ReasonLabel(r.Reason);
        var label = ActionLabel(r.Action);
        var (browser, os) = UserAgentParser.Parse(r.UserAgent);
        var device = browser == "Unknown" && os == "Unknown" ? null : $"{browser} · {os}";

        var details = new List<ActivityLogDetail>
        {
            D("ActLog_ColDateTime", CairoTime.Format(r.OccurredAt) + " " + _L["ActLog_CairoTime"].Value),
            D("ActLog_ColAction", label),
            D("ActLog_ColResult", _L[r.Success ? "ActLog_Success" : "ActLog_Failed"].Value),
        };
        if (r.UserEmail != null) details.Add(D("ActLog_ColUser", r.UserEmail));
        if (target != null) details.Add(D("ActLog_ColTarget", target));
        if (r.Portal != null) details.Add(D("Users_LoginPortal", r.Portal));
        if (reason != null) details.Add(D("ActLog_Reason", reason));
        if (r.Details != null) details.Add(D("ActLog_Details", r.Details));
        if (r.IpAddress != null) details.Add(D("Users_IpAddress", r.IpAddress));
        if (device != null) details.Add(D("Users_Device", device));

        var description = reason != null && r.Details != null ? $"{reason} · {r.Details}" : reason ?? r.Details ?? label;
        return new ActivityLogItem
        {
            Key = r.Id.ToString(),
            Time = DateTime.SpecifyKind(r.OccurredAt, DateTimeKind.Utc),
            TimeDisplay = CairoTime.Format(r.OccurredAt),
            User = r.UserEmail ?? "—",
            Action = r.Action, ActionLabel = label,
            Success = r.Success, ResultLabel = _L[r.Success ? "ActLog_Success" : "ActLog_Failed"].Value,
            Target = target, Ip = r.IpAddress, Description = description, Details = details,
        };
    }

    private ActivityLogDetail D(string labelKey, string value) => new() { Label = _L[labelKey].Value, Value = value };

    // ── Clear History ────────────────────────────────────────────────────────

    /// <summary>Resolves the request into the exact (from, to, categories) it will delete. Null = invalid.</summary>
    internal static (DateTime? From, DateTime? To, string[] Categories, string Types)? Resolve(ClearActivityLogsRequest r, DateTime nowUtc)
    {
        var types = r.Types ?? "all";
        string[] categories = types switch
        {
            "all" => ActivityCategories.All,
            ActivityCategories.Login => new[] { ActivityCategories.Login },
            ActivityCategories.Data => new[] { ActivityCategories.Data },
            ActivityCategories.Admin => new[] { ActivityCategories.Admin },
            _ => Array.Empty<string>(),
        };
        if (categories.Length == 0) return null;

        switch (r.Range)
        {
            case "last-hour": return (nowUtc.AddHours(-1), nowUtc, categories, types);
            case "last-24h": return (nowUtc.AddHours(-24), nowUtc, categories, types);
            case "last-7d": return (nowUtc.AddDays(-7), nowUtc, categories, types);
            case "last-4w": return (nowUtc.AddDays(-28), nowUtc, categories, types);
            case "all": return (null, null, categories, types);
            case "custom":
                if (r.FromDay == null || r.ToDay == null || r.FromDay > r.ToDay) return null;
                return (CairoTime.DayStartUtc(r.FromDay.Value), CairoTime.DayEndUtc(r.ToDay.Value), categories, types);
            default: return null;
        }
    }

    private static IQueryable<ActivityLog> Select(IQueryable<ActivityLog> logs, (DateTime? From, DateTime? To, string[] Categories, string Types) scope)
    {
        var categories = scope.Categories;
        logs = logs.Where(l => categories.Contains(l.Category));
        if (scope.From != null) { var f = scope.From.Value; logs = logs.Where(l => l.OccurredAt >= f); }
        if (scope.To != null) { var t = scope.To.Value; logs = logs.Where(l => l.OccurredAt <= t); }
        return logs;
    }

    private static ClearActivityLogsPreview ToPreview(int count, (DateTime? From, DateTime? To, string[] Categories, string Types) s) => new()
    {
        Count = count, Types = s.Types,
        FromDisplay = s.From == null ? null : CairoTime.Format(s.From.Value),
        ToDisplay = s.To == null ? null : CairoTime.Format(s.To.Value),
    };

    public async Task<ClearActivityLogsPreview?> PreviewClearAsync(ClearActivityLogsRequest request)
    {
        var scope = Resolve(request, DateTime.UtcNow);
        if (scope == null) return null;
        var count = await Select(_db.ActivityLogs.AsNoTracking(), scope.Value).CountAsync();
        return ToPreview(count, scope.Value);
    }

    public async Task<(int Deleted, ClearActivityLogsPreview Scope)?> ClearAsync(ClearActivityLogsRequest request)
    {
        var scope = Resolve(request, DateTime.UtcNow);
        if (scope == null) return null;
        _cache.Remove(SecurityOverviewCacheKey);
        var selected = Select(_db.ActivityLogs, scope.Value);

        int deleted;
        if (_db.Database.IsRelational())
        {
            deleted = await selected.ExecuteDeleteAsync();   // one DELETE ... WHERE <the same predicate>
        }
        else
        {
            // Providers without set-based deletes (the in-memory test database): same predicate, in batches.
            deleted = 0;
            while (true)
            {
                var batch = await selected.OrderBy(l => l.Id).Take(1000).ToListAsync();
                if (batch.Count == 0) break;
                _db.ActivityLogs.RemoveRange(batch);
                await _db.SaveChangesAsync();
                deleted += batch.Count;
            }
        }
        return (deleted, ToPreview(deleted, scope.Value));
    }
}
