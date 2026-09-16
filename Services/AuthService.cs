using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContext;

    // Per-account lockout on top of the per-IP "login" rate limiter: the
    // limiter caps request *volume* from one IP, but a patient attacker
    // spread across IPs (or just slow) could otherwise keep guessing one
    // known account indefinitely. Reuses IMemoryCache — already registered
    // (AddMemoryCache) and used elsewhere in the app — rather than adding a
    // new store, mirroring the same "N failed attempts locks it out" shape
    // OtpService already uses for password-reset codes.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    private static string FailKey(string email) => $"login-fail:{email.Trim().ToLowerInvariant()}";

    public AuthService(AppDbContext db, ILogger<AuthService> logger, IMemoryCache cache, IHttpContextAccessor httpContext)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
        _httpContext = httpContext;
    }

    public async Task<(User? User, string? FailReason, bool IsLockedOut)> ValidateAsync(string email, string password, string portal)
    {
        var failKey = FailKey(email);
        if (_cache.TryGetValue(failKey, out int failCount) && failCount >= MaxFailedAttempts)
        {
            _logger.LogWarning("Login blocked: too many recent failed attempts for '{Email}'.", email.ToLower());
            // IsLockedOut is safe to surface distinctly to the caller: the
            // lockout counter is keyed on the raw email string before the DB
            // is even queried, so it fires identically for a made-up address
            // hammered 5 times as for a real one — no account-existence
            // signal leaks from telling the user "try again later" instead of
            // "wrong password". Not logged to login_history either — there's
            // no cheap way to resolve the account here without undoing the
            // whole point of checking the lockout before touching the
            // database, and every attempt that built up to this lockout was
            // already logged individually below.
            return (null, "Account temporarily locked after repeated failed attempts.", true);
        }

        void RecordFailure() => _cache.Set(failKey, failCount + 1, LockoutWindow);

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());

        if (user == null)
        {
            var reason = $"No user found for email '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            // Not logged to login_history — there's no account to attach the
            // row to, and logging it under some placeholder would let this
            // page be used to probe which emails are registered.
            return (null, reason, false);
        }
        // Bulk-created accounts start with no password set (pending activation
        // via the OTP flow) — reject the login attempt instead of letting
        // BCrypt.Verify throw on a null/empty hash.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            var reason = $"User '{email.ToLower()}' has no password hash set.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            await LogAttemptAsync(user, portal, success: false, "no-password-set");
            return (null, reason, false);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var reason = $"Password mismatch for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            await LogAttemptAsync(user, portal, success: false, "wrong-password");
            return (null, reason, false);
        }

        _cache.Remove(failKey);
        await LogAttemptAsync(user, portal, success: true, null);
        return (user, null, false);
    }

    public void ClearLockout(string email) => _cache.Remove(FailKey(email));

    // Records the attempt once it's resolved against a known account — shared
    // by both the Home and Admin AccountControllers' Login actions, since they
    // both call ValidateAsync. Best-effort: a failure here must never block
    // the login itself, so it's swallowed (logged) rather than surfaced to
    // the caller.
    private async Task LogAttemptAsync(User user, string portal, bool success, string? failureReason)
    {
        try
        {
            var http = _httpContext.HttpContext;
            _db.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id,
                Email = user.Email,
                Portal = portal,
                Success = success,
                FailureReason = failureReason,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = http?.Request.Headers.UserAgent.ToString(),
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record login history for user {UserId}.", user.Id);
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash)) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        ClearLockout(user.Email);
        return true;
    }

    public async Task<bool> SetPasswordAsync(int userId, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        ClearLockout(user.Email);
        return true;
    }
}
