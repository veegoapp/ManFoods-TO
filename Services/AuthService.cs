using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Extensions;
using MvcApp.Models;

namespace MvcApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ISessionValidationService _sessionValidation;

    // Failed-login handling, on top of the per-IP "login" rate limiter (which caps
    // request *volume* from one IP but says nothing about which account is targeted).
    //
    // The old design kept one failure counter per email, so anyone on the internet
    // could lock a known account (the Super Admin's address is public) by simply
    // sending a few bad passwords, over and over. Now:
    //
    //  1. Per account AND client IP: MaxFailedAttempts bad passwords within
    //     LockoutWindow block further attempts for that account from that IP only —
    //     the account owner signing in from anywhere else is unaffected, and so is
    //     everyone else behind the same IP (they are signing in to other accounts).
    //     It is a sliding window, not a lock that refreshes itself: the block lifts
    //     as the oldest failures age out, so it can never be kept up indefinitely.
    //  2. Per account across all IPs (distributed guessing): once
    //     GlobalFailedAttemptsThreshold bad passwords land within the window, attempts
    //     from IPs that have NOT successfully signed in to that account within
    //     KnownIpWindow are blocked. Sign-ins from an IP the owner already uses keep
    //     working, so an attacker can slow strangers down but cannot lock the owner out.
    //
    // The counters are read from login_history (already written for every attempt
    // against a known account), so they are shared by every app instance and survive
    // restarts. Attempts against unknown emails have no account to attach a row to,
    // so a small in-memory per-(email, IP) counter is kept for those only to keep the
    // "too many attempts" response identical for real and made-up addresses.
    private const int MaxFailedAttempts = 5;
    private const int GlobalFailedAttemptsThreshold = 50;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan KnownIpWindow = TimeSpan.FromDays(30);
    private const string TempPasswordExpiredReason = "temp-password-expired";
    private static string UnknownEmailFailKey(string email, string? ip) => $"login-fail-unknown:{email.Trim().ToLowerInvariant()}|{ip}";
    private static string LockoutResetKey(string email) => $"login-fail-reset:{email.Trim().ToLowerInvariant()}";

    public AuthService(AppDbContext db, ILogger<AuthService> logger, IMemoryCache cache, IHttpContextAccessor httpContext, ISessionValidationService sessionValidation)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
        _httpContext = httpContext;
        _sessionValidation = sessionValidation;
    }

    // A made-up or password-less account would otherwise skip BCrypt entirely and answer in a few
    // milliseconds while a real account takes ~100+ ms — enough to tell them apart by timing alone.
    // This hash is computed once with the same (default) work factor the app uses for every real
    // hash, and checked on those paths purely to spend the same time; the result is ignored.
    private static readonly Lazy<string> TimingEqualizerHash =
        new(() => BCrypt.Net.BCrypt.HashPassword("timing-equalizer-" + Guid.NewGuid()));
    internal static void SpendPasswordCheckTime(string? password) =>
        BCrypt.Net.BCrypt.Verify(password ?? "", TimingEqualizerHash.Value);

    private string? ClientIp() => _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static readonly (User? User, string? FailReason, bool IsLockedOut, bool IsTempPasswordExpired) LockedOut =
        (null, "Account temporarily locked after repeated failed attempts.", true, false);

    /// <summary>Whether this client IP may attempt to sign in to this account right now —
    /// see the policy described on the constants above.</summary>
    private async Task<bool> IsBlockedAsync(User user, string? ip)
    {
        var now = DateTime.UtcNow;
        var since = now - LockoutWindow;
        // A password reset (ClearLockout) makes earlier failures irrelevant to the new password.
        if (_cache.TryGetValue(LockoutResetKey(user.Email), out DateTime resetAt) && resetAt > since) since = resetAt;

        // A successful sign-in from this IP also starts a fresh count for it.
        var lastSuccessFromIp = await _db.LoginHistories
            .Where(l => l.UserId == user.Id && l.Success && l.IpAddress == ip && l.LoggedInAt > since)
            .MaxAsync(l => (DateTime?)l.LoggedInAt);
        var ipSince = lastSuccessFromIp ?? since;
        var failuresFromIp = await _db.LoginHistories
            .CountAsync(l => l.UserId == user.Id && !l.Success && l.FailureReason != TempPasswordExpiredReason
                             && l.IpAddress == ip && l.LoggedInAt > ipSince);
        if (failuresFromIp >= MaxFailedAttempts) return true;

        var failuresFromAnyIp = await _db.LoginHistories
            .CountAsync(l => l.UserId == user.Id && !l.Success && l.FailureReason != TempPasswordExpiredReason && l.LoggedInAt > since);
        if (failuresFromAnyIp < GlobalFailedAttemptsThreshold) return false;

        var knownSince = now - KnownIpWindow;
        var knownIp = await _db.LoginHistories
            .AnyAsync(l => l.UserId == user.Id && l.Success && l.IpAddress == ip && l.LoggedInAt > knownSince);
        return !knownIp;
    }

    public async Task<(User? User, string? FailReason, bool IsLockedOut, bool IsTempPasswordExpired)> ValidateAsync(string email, string password, string portal)
    {
        var ip = ClientIp();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());

        if (user == null)
        {
            var unknownKey = UnknownEmailFailKey(email, ip);
            _cache.TryGetValue(unknownKey, out int unknownFailCount);
            if (unknownFailCount >= MaxFailedAttempts)
            {
                _logger.LogWarning("Login blocked: too many recent failed attempts for '{Email}'.", MaskEmail(email));
                return LockedOut;
            }

            SpendPasswordCheckTime(password);
            var reason = $"No user found for email '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: no user found for '{Email}'.", MaskEmail(email));
            _cache.Set(unknownKey, unknownFailCount + 1, LockoutWindow);
            // Not logged to login_history — there's no account to attach the
            // row to, and logging it under some placeholder would let this
            // page be used to probe which emails are registered.
            return (null, reason, false, false);
        }

        // IsLockedOut is surfaced distinctly so a real user knows to wait. It is safe
        // to do so: made-up addresses reach the same message after the same number of
        // attempts (see UnknownEmailFailKey above), so it does not reveal that an
        // account exists. Blocked attempts are not written to login_history — every
        // attempt that built up to the block already was, and writing them too would
        // let a flood push the owner's own sign-in history out of the retention cap.
        if (await IsBlockedAsync(user, ip))
        {
            _logger.LogWarning("Login blocked: too many recent failed attempts for '{Email}'.", MaskEmail(user.Email));
            return LockedOut;
        }

        // Bulk-created accounts start with no password set (pending activation
        // via the OTP flow) — reject the login attempt instead of letting
        // BCrypt.Verify throw on a null/empty hash.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            SpendPasswordCheckTime(password);
            var reason = $"User '{email.ToLower()}' has no password hash set.";
            _logger.LogWarning("Login failed: '{Email}' has no password hash set.", MaskEmail(email));
            await LogAttemptAsync(user, portal, success: false, "no-password-set");
            return (null, reason, false, false);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var reason = $"Password mismatch for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: password mismatch for '{Email}'.", MaskEmail(email));
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
            _logger.LogWarning("Login failed: temporary password expired for '{Email}'.", MaskEmail(email));
            await LogAttemptAsync(user, portal, success: false, TempPasswordExpiredReason);
            return (null, reason, false, true);
        }

        await LogAttemptAsync(user, portal, success: true, null);
        return (user, null, false, false);
    }

    /// <summary>Log-safe form of an email (first character + domain) — enough to correlate attempts without
    /// writing a full address, which for unknown accounts is attacker-supplied text, into the logs.</summary>
    internal static string MaskEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        var at = e.IndexOf('@');
        if (at <= 0) return e.Length == 0 ? "(empty)" : "***";
        return e[0] + "***" + new string(e[at..].Where(c => !char.IsControl(c)).ToArray());
    }

    /// <summary>Makes failures recorded before now irrelevant to the lockout check — called after a
    /// password reset. Held in this instance's memory only: it merely relaxes the count after a
    /// legitimate reset, so another instance not seeing it is harmless (the window is 15 minutes).</summary>
    public void ClearLockout(string email) => _cache.Set(LockoutResetKey(email), DateTime.UtcNow, LockoutWindow);

    // How long a login_history row is kept, and how many rows are kept per
    // user regardless of age — the table otherwise grows forever (nothing
    // else in the app ever deletes from it; there's no background-job
    // infrastructure to run a scheduled purge instead).
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(365);
    private const int MaxRowsPerUser = 500; // each for successful and for failed attempts

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
                IpAddress = ClientIp(),
                UserAgent = InputLimits.Clip(http?.Request.Headers.UserAgent.ToString(), InputLimits.UserAgent),
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
        // Kept separately for successful and failed attempts: the lockout check relies on
        // successful rows ("IPs this account really signs in from"), and a burst of failed
        // guesses must not be able to push those out of the cap.
        var successIdsToKeep = await _db.LoginHistories
            .Where(l => l.UserId == userId && l.Success)
            .OrderByDescending(l => l.LoggedInAt)
            .Take(MaxRowsPerUser)
            .Select(l => l.Id)
            .ToListAsync();
        var failureIdsToKeep = await _db.LoginHistories
            .Where(l => l.UserId == userId && !l.Success)
            .OrderByDescending(l => l.LoggedInAt)
            .Take(MaxRowsPerUser)
            .Select(l => l.Id)
            .ToListAsync();
        var idsToKeep = successIdsToKeep.Concat(failureIdsToKeep).ToList();
        await _db.LoginHistories
            .Where(l => l.UserId == userId && !idsToKeep.Contains(l.Id))
            .ExecuteDeleteAsync();

        if (Random.Shared.Next(100) == 0)
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            await _db.LoginHistories.Where(l => l.LoggedInAt < cutoff).ExecuteDeleteAsync();
        }
    }

    // Every password change ends all of the account's OTHER sessions (each carries a
    // stamp of the password it was opened with — see PasswordFingerprint). When the
    // person changing it is the one signed in on this request, their own session is
    // re-stamped so they stay signed in; anyone else's session, and a session opened
    // by whoever knew the old password, stops working.
    private void OnPasswordChanged(User user)
    {
        ClearLockout(user.Email);
        _sessionValidation.Invalidate(user.Id);

        var session = _httpContext.HttpContext?.Features.Get<ISessionFeature>()?.Session;
        if (session != null && session.GetUserId() == user.Id)
            session.SetPasswordFingerprint(PasswordFingerprint.Compute(user.PasswordHash));
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
        OnPasswordChanged(user);
        return true;
    }

    public async Task<bool> SetPasswordAsync(int userId, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        // Only for an account still on a system-generated temporary password: a
        // leftover setup token must not be able to overwrite an already-chosen password.
        if (user == null || !user.MustChangePassword) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TempPasswordExpiresAt = null;
        await _db.SaveChangesAsync();
        OnPasswordChanged(user);
        return true;
    }
}
