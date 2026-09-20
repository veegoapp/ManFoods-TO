using MvcApp.Models;

namespace MvcApp.Services;

public interface IAuthService
{
    /// <summary>`portal` is "Home" or "Admin" — which login form the attempt came
    /// through, recorded on the resulting login-history row (success or failure)
    /// for any attempt against a known account.</summary>
    Task<(User? User, string? FailReason, bool IsLockedOut, bool IsTempPasswordExpired)> ValidateAsync(string email, string password, string portal);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    /// <summary>Sets a new password without verifying the old one — only for the
    /// forced first-login flow (a temporary/OTP-issued password), where the
    /// caller has already proven possession of the current credential by
    /// authenticating with it to reach this session in the first place.</summary>
    Task<bool> SetPasswordAsync(int userId, string newPassword);
    /// <summary>Clears the failed-login counter for an email, so a password that
    /// was just reset (by the user themselves, an admin-issued OTP, or the
    /// Super Admin recovery key) isn't still rejected by a lockout window that
    /// was building up against the old, now-irrelevant password.</summary>
    void ClearLockout(string email);
}
