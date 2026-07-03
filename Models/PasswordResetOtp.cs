using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

[Table("password_reset_otps")]
public class PasswordResetOtp
{
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("otp_code")]
    public string OtpCode { get; set; } = "";

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("is_used")]
    public bool IsUsed { get; set; }

    [Column("failed_attempts")]
    public int FailedAttempts { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
