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

    public async Task<(User? User, string? FailReason, bool IsLockedOut, bool IsTempPasswordExpired)> ValidateAsync(string email, string password, string portal)
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
            return (null, "Account temporarily locked after repeated failed attempts.", true, false);
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
            return (null, reason, false, false);
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
            return (null, reason, false, false);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var reason = $"Password mismatch for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            await LogAttemptAsync(user, portal, success: false, "wrong-password");
            return (null, reason, false, false);
        }

        // A system-generated temporary password (Add User, single/bulk
        // "Generate Default Password") is only good for 24 hours from
        // issuance — past that, the correct temp password still won't let
        // them in; an admin has to regenerate a new one.
        if (user.MustChangePassword && user.TempPasswordExpiresAt.HasValue && user.TempPasswordExpiresAt.Value <= DateTime.UtcNow)
        {
            var reason = $"Temporary password expired for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            await LogAttemptAsync(user, portal, success: false, "temp-password-expired");
            return (null, reason, false, true);
        }

        _cache.Remove(failKey);
        await LogAttemptAsync(user, portal, success: true, null);
        return (user, null, false, false);
    }

    public void ClearLockout(string email) => _cache.Remove(FailKey(email));

    // How long a login_history row is kept, and how many rows are kept per
    // user regardless of age — the table otherwise grows forever (nothing
    // else in the app ever deletes from it; there's no background-job
    // infrastructure to run a scheduled purge instead).
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(365);
    private const int MaxRowsPerUser = 500;

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
            await PruneHistoryAsync(user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record login history for user {UserId}.", user.Id);
        }
    }

    // Opportunistic cleanup, piggybacked on the same request that just wrote
    // a row, so it doesn't need its own schedule. Failures here must never
    // surface past LogAttemptAsync, so this stays inside that method's
    // try/catch rather than getting one of its own.
    private async Task PruneHistoryAsync(int userId)
    {
        // The per-user cap is cheap (scoped by the indexed user_id) so it
        // runs every time. The age-based purge has no index to lean on —
        // logged_in_at is only indexed together with user_id — so it's a
        // full-table scan; only worth paying for occasionally.
        var idsToKeep = await _db.LoginHistories
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.LoggedInAt)
            .Take(MaxRowsPerUser)
            .Select(l => l.Id)
            .ToListAsync();
        await _db.LoginHistories
            .Where(l => l.UserId == userId && !idsToKeep.Contains(l.Id))
            .ExecuteDeleteAsync();

        if (Random.Shared.Next(100) == 0)
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            await _db.LoginHistories.Where(l => l.LoggedInAt < cutoff).ExecuteDeleteAsync();
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash)) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TempPasswordExpiresAt = null;
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
        user.TempPasswordExpiresAt = null;
        await _db.SaveChangesAsync();
        ClearLockout(user.Email);
        return true;
    }
}
