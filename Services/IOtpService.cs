namespace MvcApp.Services;

public interface IOtpService
{
    /// <summary>Generates a compliant random temporary password (PasswordPolicy)
    /// for every non-Admin account still pending activation — either it has no
    /// password set yet, or it does but hasn't completed its first-login change
    /// yet (already-activated accounts are skipped entirely) — sets it as the
    /// account's real password hash directly (no OTP involved, replacing
    /// whatever temporary password was there before), and marks the account
    /// MustChangePassword so the owner is forced to replace it on first login.
    /// Returns the count generated and an Excel file (Email, Phone, Temporary
    /// Password, and a ready-to-send SMS Message column combining a welcome
    /// note, the portal link, and both).</summary>
    Task<(int count, byte[] excelBytes)> GenerateBulkDefaultPasswordsAsync();

    /// <summary>Same as GenerateBulkDefaultPasswordsAsync but for one specific
    /// non-Admin account still pending activation — regenerates (and thereby
    /// invalidates) its temporary password instead of running the bulk sweep.
    /// Returns null if the user doesn't exist, is an Admin, or has already
    /// completed activation.</summary>
    Task<string?> GenerateSingleDefaultPasswordAsync(int userId);

    /// <summary>Generates a fresh OTP for one specific User-role account,
    /// regardless of whether it already has a password (forgot-password case).
    /// Returns null if the user doesn't exist or isn't a User-role account.</summary>
    Task<string?> GenerateSingleOtpAsync(int userId);

    /// <summary>Verifies an OTP against the account matched by email or phone
    /// and, on success, sets the new password. On a wrong code, increments the
    /// attempt counter and invalidates the OTP after 5 failures. Never matches
    /// an Admin account — Admin password resets go through the
    /// Generate/VerifyAndResetAdminPasswordAsync pair below instead.</summary>
    Task<(bool success, string message)> VerifyAndResetPasswordAsync(string identifier, string otpCode, string newPassword);

    /// <summary>Super-Admin-only: generates a password-reset OTP for another
    /// (non-Super-Admin) Admin account, using the same 24-hour-expiry /
    /// one-active-code rules as the User OTP flow. Returns null if the
    /// requester isn't the Super Admin, the target isn't an Admin, or the
    /// target is the Super Admin itself (who must use the Master Recovery Key).</summary>
    Task<string?> GenerateAdminResetOtpAsync(int targetUserId, string requestingEmail);

    /// <summary>Verifies an Admin-reset OTP and, on success, sets the new
    /// password — this only ever resets the password, never logs the Admin
    /// in. Only matches Admin accounts other than the Super Admin (mirrors
    /// VerifyAndResetPasswordAsync's User-only matching, inverted), so this
    /// cannot be used to reset a User or the Super Admin's own account.</summary>
    Task<(bool success, string message)> VerifyAndResetAdminPasswordAsync(string identifier, string otpCode, string newPassword);
}
