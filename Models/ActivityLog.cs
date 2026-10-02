using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One row per recorded portal activity — the single source behind the Super Admin "Activity Logs" page.
/// Holds only what is needed to understand an event; by design there is no column that could carry a
/// password, password hash, OTP code, recovery key or token, and the writer never accepts such values.
/// Times are UTC; the page converts them to Africa/Cairo for display.
/// </summary>
[Table("activity_logs")]
public class ActivityLog
{
    [Column("id")]
    public long Id { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>"login", "data" or "admin" — what "Clear History" selects by.</summary>
    [Column("category")]
    public string Category { get; set; } = "";

    /// <summary>Stable action code, e.g. "login-success", "login-unknown", "upload", "user.create" (see ActivityActions).</summary>
    [Column("action")]
    public string Action { get; set; } = "";

    [Column("success")]
    public bool Success { get; set; } = true;

    /// <summary>The person who did it: the email typed on a login attempt (even when no such account exists),
    /// or the signed-in admin for admin actions. Null when not known.</summary>
    [Column("user_email")]
    public string? UserEmail { get; set; }

    [Column("actor_user_id")]
    public int? ActorUserId { get; set; }

    /// <summary>The account an admin action was about, when there is one.</summary>
    [Column("target_user_id")]
    public int? TargetUserId { get; set; }

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    /// <summary>"Home" or "Admin" for login attempts.</summary>
    [Column("portal")]
    public string? Portal { get; set; }

    /// <summary>Short reason code for a failure/denial (e.g. "wrong-password", "locked-out").</summary>
    [Column("reason")]
    public string? Reason { get; set; }

    /// <summary>Short plain-text facts (file type and period, new role, counts...). Never a secret.</summary>
    [Column("details")]
    public string? Details { get; set; }
}

public static class ActivityCategories
{
    public const string Login = "login";
    public const string Data = "data";
    public const string Admin = "admin";
    public static readonly string[] All = { Login, Data, Admin };
}

/// <summary>Every action code the application writes.</summary>
public static class ActivityActions
{
    public const string LoginSuccess = "login-success";
    public const string LoginFailed = "login-failed";
    public const string LoginUnknown = "login-unknown";
    public const string LoginBlocked = "login-blocked";
    public const string Logout = "logout";

    public const string Upload = "upload";
    public const string UploadLogDelete = "upload-log.delete";

    public const string UserCreate = "user.create";
    public const string UserUpdate = "user.update";
    public const string UserDelete = "user.delete";
    public const string RoleChange = "role.change";
    public const string UserBulkUpload = "user.bulk-upload";
    public const string PasswordGenerate = "password.generate";
    public const string PasswordGenerateBulk = "password.generate-bulk";
    public const string OtpGenerate = "otp.generate";
    public const string OtpGenerateAdmin = "otp.generate-admin";
    public const string RecoveryKeyRegenerate = "recovery-key.regenerate";
    public const string RecoveryKeyUse = "recovery-key.use";
    public const string SettingsAccessPolicy = "settings.access-policy";
    public const string SettingsPageVisibility = "settings.page-visibility";
    public const string ActivityLogsClear = "activity-logs.clear";
    public const string AccessDenied = "access.denied";

    /// <summary>Which "Clear History" type an action belongs to.</summary>
    public static string CategoryOf(string action) => action switch
    {
        LoginSuccess or LoginFailed or LoginUnknown or LoginBlocked or Logout => ActivityCategories.Login,
        Upload or UploadLogDelete => ActivityCategories.Data,
        _ => ActivityCategories.Admin,
    };
}
