namespace MvcApp.Services;

public interface IOtpService
{
    /// <summary>Generates a fresh OTP for every User-role account that has no
    /// password set yet (already-activated accounts are skipped entirely).
    /// Returns the count generated and an Excel file (Email, Phone, OTP).</summary>
    Task<(int count, byte[] excelBytes)> GenerateBulkOtpsAsync();

    /// <summary>Generates a fresh OTP for one specific User-role account,
    /// regardless of whether it already has a password (forgot-password case).
    /// Returns null if the user doesn't exist or isn't a User-role account.</summary>
    Task<string?> GenerateSingleOtpAsync(int userId);

    /// <summary>Verifies an OTP against the account matched by email or phone
    /// and, on success, sets the new password. On a wrong code, increments the
    /// attempt counter and invalidates the OTP after 5 failures.</summary>
    Task<(bool success, string message)> VerifyAndResetPasswordAsync(string identifier, string otpCode, string newPassword);
}
