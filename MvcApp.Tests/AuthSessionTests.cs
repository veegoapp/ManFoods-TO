using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MvcApp.Data;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models;
using MvcApp.Models.ViewModels;
using MvcApp.Resources;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Login lockout, session invalidation on password change/reset, absolute session
/// lifetime, OTP generation/verification and one-time tokens.</summary>
public class AuthSessionTests
{
    private const string OldPassword = "Old-Password-123!";
    private const string NewPassword = "New-Password-456!";
    private const string SuperAdminEmail = SuperAdminPolicy.SuperAdminEmail;

    // ── test plumbing ────────────────────────────────────────────────────────

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _store.TryGetValue(key, out value);
    }

    private sealed class FakeSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = default!;
    }

    private sealed class FakeLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    }

    /// <summary>One "server": a database, caches, and a request context whose client IP/session can be swapped.</summary>
    private sealed class Env
    {
        public AppDbContext Db { get; }
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public DefaultHttpContext Http { get; } = new();
        public SessionValidationService Sessions { get; }
        public AuthService Auth { get; }

        public Env(string? databaseName = null)
        {
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString()).Options);
            Http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            Http.Features.Set<ISessionFeature>(new FakeSessionFeature { Session = new FakeSession() });
            var accessor = new HttpContextAccessor { HttpContext = Http };
            Sessions = new SessionValidationService(Db, Cache);
            Auth = new AuthService(Db, NullLogger<AuthService>.Instance, Cache, accessor, Sessions);
        }

        public ISession Session => Http.Session;
        public void From(string ip) => Http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        public void UseNewSession() => Http.Features.Set<ISessionFeature>(new FakeSessionFeature { Session = new FakeSession() });

        public OtpService Otp() => new(Db, new FakeLocalizer(), NullLogger<OtpService>.Instance, Auth, Sessions);
        public UserService Users() => new(Db, new StoreAccessService(Db), Sessions, Auth);

        public async Task<User> AddUserAsync(string email, string role = "User", string password = OldPassword, bool mustChange = false)
        {
            var user = new User
            {
                Email = email, Phone = "+2010" + Math.Abs(email.GetHashCode()), Role = role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4), MustChangePassword = mustChange,
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        public async Task AddHistoryAsync(User user, string? ip, bool success, DateTime at, string? reason = null)
        {
            Db.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id, Email = user.Email, Portal = "Home", IpAddress = ip, Success = success,
                FailureReason = success ? null : (reason ?? "wrong-password"), LoggedInAt = at,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>Opens a "session" for the user the way a login does and returns its password stamp.</summary>
        public string SignIn(User user)
        {
            var fingerprint = PasswordFingerprint.Compute(user.PasswordHash);
            Session.SetUserSession(user.Id, user.Email, user.Role, user.AssignedName, user.MustChangePassword, fingerprint);
            return fingerprint;
        }
    }

    private static async Task FailLoginsAsync(Env env, string email, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (user, _, locked, _) = await env.Auth.ValidateAsync(email, "wrong-password-!", "Home");
            Assert.Null(user);
            Assert.False(locked, $"attempt {i + 1} should still reach the password check");
        }
    }

    // ── 1. login lockout ─────────────────────────────────────────────────────

    [Fact]
    public async Task NormalLogin_StillWorks_AndIsRecorded()
    {
        var env = new Env();
        await env.AddUserAsync("worker@example.com");

        var (user, reason, locked, expired) = await env.Auth.ValidateAsync("Worker@Example.com", OldPassword, "Home");

        Assert.NotNull(user);
        Assert.Null(reason);
        Assert.False(locked);
        Assert.False(expired);
        Assert.Single(env.Db.LoginHistories.Where(l => l.Success));
    }

    [Fact]
    public async Task FiveBadPasswords_BlockThatIpOnly_NotTheAccountOwnerElsewhere()
    {
        var env = new Env();
        await env.AddUserAsync(SuperAdminEmail, "Admin");

        // The attacker's IP: 5 wrong guesses, then even the right password is refused from there.
        env.From("198.51.100.7");
        await FailLoginsAsync(env, SuperAdminEmail, 5);
        var blocked = await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin");
        Assert.Null(blocked.User);
        Assert.True(blocked.IsLockedOut);

        // The real owner, from their own network, is not affected at all.
        env.From("203.0.113.99");
        var owner = await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin");
        Assert.NotNull(owner.User);
        Assert.False(owner.IsLockedOut);
    }

    [Fact]
    public async Task RepeatedAttack_CannotKeepTheSuperAdminLockedOut()
    {
        var env = new Env();
        var admin = await env.AddUserAsync(SuperAdminEmail, "Admin");
        env.From("198.51.100.7");

        await FailLoginsAsync(env, SuperAdminEmail, 5);
        Assert.True((await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin")).IsLockedOut);

        // Hammering while blocked does not extend the block: blocked attempts are not recorded...
        for (var i = 0; i < 20; i++) await env.Auth.ValidateAsync(SuperAdminEmail, "still-wrong-!", "Admin");
        Assert.Equal(5, env.Db.LoginHistories.Count(l => l.UserId == admin.Id && !l.Success));

        // ...so once the window has passed, the block is gone.
        foreach (var row in env.Db.LoginHistories.Where(l => l.UserId == admin.Id)) row.LoggedInAt = DateTime.UtcNow.AddMinutes(-16);
        await env.Db.SaveChangesAsync();
        var after = await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin");
        Assert.NotNull(after.User);
    }

    [Fact]
    public async Task OtherAccountsBehindTheSameIp_AreNotAffected()
    {
        var env = new Env();
        await env.AddUserAsync("victim@example.com");
        await env.AddUserAsync("colleague@example.com");
        env.From("198.51.100.20"); // one shared office/NAT address

        await FailLoginsAsync(env, "victim@example.com", 5);
        Assert.True((await env.Auth.ValidateAsync("victim@example.com", OldPassword, "Home")).IsLockedOut);

        var colleague = await env.Auth.ValidateAsync("colleague@example.com", OldPassword, "Home");
        Assert.NotNull(colleague.User);
    }

    [Fact]
    public async Task DistributedGuessing_SlowsStrangers_ButNeverLocksOutAnIpTheOwnerAlreadyUses()
    {
        var env = new Env();
        var admin = await env.AddUserAsync(SuperAdminEmail, "Admin");
        const string ownerIp = "203.0.113.50";
        await env.AddHistoryAsync(admin, ownerIp, success: true, DateTime.UtcNow.AddDays(-3));
        // 50 wrong guesses in the last few minutes, each from a different address (a botnet).
        for (var i = 0; i < 50; i++)
            await env.AddHistoryAsync(admin, $"192.0.2.{i + 1}", success: false, DateTime.UtcNow.AddMinutes(-2));

        env.From("198.51.100.200"); // a stranger with the right password is held back during the attack
        var stranger = await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin");
        Assert.Null(stranger.User);
        Assert.True(stranger.IsLockedOut);

        env.From(ownerIp); // the owner's usual network still works
        var owner = await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin");
        Assert.NotNull(owner.User);
    }

    [Fact]
    public async Task DistributedGuessing_ProtectionLifts_OnceTheAttackStops()
    {
        var env = new Env();
        var admin = await env.AddUserAsync(SuperAdminEmail, "Admin");
        for (var i = 0; i < 50; i++)
            await env.AddHistoryAsync(admin, $"192.0.2.{i + 1}", success: false, DateTime.UtcNow.AddMinutes(-20));

        env.From("198.51.100.200");
        Assert.NotNull((await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin")).User);
    }

    [Fact]
    public async Task SuccessfulLogin_StartsAFreshCount()
    {
        var env = new Env();
        await env.AddUserAsync("worker@example.com");

        await FailLoginsAsync(env, "worker@example.com", 4);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User);
        await FailLoginsAsync(env, "worker@example.com", 4); // would be 8 without the reset
    }

    [Fact]
    public async Task ExpiredTemporaryPassword_DoesNotCountTowardLockout()
    {
        var env = new Env();
        var user = await env.AddUserAsync("new@example.com", mustChange: true);
        user.TempPasswordExpiresAt = DateTime.UtcNow.AddHours(-1);
        await env.Db.SaveChangesAsync();

        for (var i = 0; i < 8; i++)
        {
            var result = await env.Auth.ValidateAsync("new@example.com", OldPassword, "Home");
            Assert.True(result.IsTempPasswordExpired);
            Assert.False(result.IsLockedOut);
        }
    }

    [Fact]
    public async Task UnknownEmail_GetsTheSameLockedResponse_SoAccountExistenceIsNotRevealed()
    {
        var env = new Env();
        for (var i = 0; i < 5; i++) Assert.False((await env.Auth.ValidateAsync("nobody@example.com", "x", "Home")).IsLockedOut);
        Assert.True((await env.Auth.ValidateAsync("nobody@example.com", "x", "Home")).IsLockedOut);
    }

    [Fact]
    public async Task ClearLockout_AfterAPasswordReset_LiftsTheBlock()
    {
        var env = new Env();
        await env.AddUserAsync("worker@example.com");
        env.From("198.51.100.7");
        await FailLoginsAsync(env, "worker@example.com", 5);
        Assert.True((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).IsLockedOut);

        env.Auth.ClearLockout("worker@example.com");

        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User);
    }

    // ── 2. session invalidation on password change / reset ───────────────────

    [Fact]
    public async Task OwnPasswordChange_EndsOtherSessions_ButKeepsTheCurrentOne()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        var currentFingerprint = env.SignIn(user);
        var otherSessionFingerprint = currentFingerprint; // e.g. a browser that was left signed in elsewhere
        Assert.True(await env.Sessions.IsValidAsync(user.Id, user.Role, otherSessionFingerprint));

        Assert.True(await env.Auth.ChangePasswordAsync(user.Id, OldPassword, NewPassword));

        Assert.False(await env.Sessions.IsValidAsync(user.Id, user.Role, otherSessionFingerprint));
        Assert.True(await env.Sessions.IsValidAsync(user.Id, user.Role, env.Session.GetPasswordFingerprint()));
        Assert.NotEqual(currentFingerprint, env.Session.GetPasswordFingerprint());
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ChangesNothing()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        var fingerprint = env.SignIn(user);

        Assert.False(await env.Auth.ChangePasswordAsync(user.Id, "not-my-password", NewPassword));

        Assert.True(await env.Sessions.IsValidAsync(user.Id, user.Role, fingerprint));
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User);
    }

    [Fact]
    public async Task AfterPasswordChange_OldPasswordFails_AndNewPasswordLogsIn()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        env.SignIn(user);
        await env.Auth.ChangePasswordAsync(user.Id, OldPassword, NewPassword);

        Assert.Null((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", NewPassword, "Home")).User);
    }

    [Fact]
    public async Task AdminResettingAnotherUsersPassword_EndsThatUsersSessions()
    {
        var env = new Env();
        await env.AddUserAsync(SuperAdminEmail, "Admin");
        var target = await env.AddUserAsync("worker@example.com");
        var targetFingerprint = PasswordFingerprint.Compute(target.PasswordHash);
        Assert.True(await env.Sessions.IsValidAsync(target.Id, target.Role, targetFingerprint)); // primes the cache

        var (updated, error) = await env.Users().UpdateAsync(target.Id,
            new EditUserViewModel { Email = target.Email, Phone = target.Phone, Role = target.Role, Password = NewPassword, ConfirmPassword = NewPassword },
            SuperAdminEmail);

        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.False(await env.Sessions.IsValidAsync(target.Id, target.Role, targetFingerprint));
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", NewPassword, "Home")).User);
    }

    [Fact]
    public async Task EditingAUserWithoutAPassword_LeavesTheirSessionsAlone()
    {
        var env = new Env();
        await env.AddUserAsync(SuperAdminEmail, "Admin");
        var target = await env.AddUserAsync("worker@example.com");
        var fingerprint = PasswordFingerprint.Compute(target.PasswordHash);

        var (_, error) = await env.Users().UpdateAsync(target.Id,
            new EditUserViewModel { Email = target.Email, Phone = "+201000000999", Role = target.Role }, SuperAdminEmail);

        Assert.Null(error);
        Assert.True(await env.Sessions.IsValidAsync(target.Id, target.Role, fingerprint));
    }

    [Fact]
    public async Task AdminCannotSetTheirOwnPasswordFromTheEditForm_WithoutTheCurrentOne()
    {
        var env = new Env();
        var admin = await env.AddUserAsync("ops-admin@example.com", "Admin");
        var originalHash = admin.PasswordHash;

        var (updated, error) = await env.Users().UpdateAsync(admin.Id,
            new EditUserViewModel { Email = admin.Email, Phone = admin.Phone, Role = "Admin", Password = NewPassword, ConfirmPassword = NewPassword },
            "ops-admin@example.com");

        Assert.Null(updated);
        Assert.Equal("use-change-password", error);
        Assert.Equal(originalHash, (await env.Db.Users.FindAsync(admin.Id))!.PasswordHash);
    }

    [Fact]
    public async Task AdminCanStillEditTheirOwnDetails_WhenNotChangingThePassword()
    {
        var env = new Env();
        var admin = await env.AddUserAsync("ops-admin@example.com", "Admin");
        await env.AddUserAsync(SuperAdminEmail, "Admin"); // keeps "last admin" out of the picture

        var (updated, error) = await env.Users().UpdateAsync(admin.Id,
            new EditUserViewModel { Email = admin.Email, Phone = "+201000000555", Role = "Admin", AssignedName = "Ops" },
            "ops-admin@example.com");

        Assert.Null(error);
        Assert.NotNull(updated);
    }

    [Fact]
    public async Task AdminCannotTakeTheSuperAdminIdentity_ByPaddingTheEmailWithWhitespace()
    {
        var env = new Env();
        await env.AddUserAsync(SuperAdminEmail, "Admin");
        var other = await env.AddUserAsync("ops-admin@example.com", "Admin");

        var (updated, error) = await env.Users().UpdateAsync(other.Id,
            new EditUserViewModel { Email = " " + SuperAdminEmail, Phone = other.Phone, Role = "Admin" }, "ops-admin@example.com");

        Assert.Null(updated);
        Assert.Equal("duplicate-email", error);
    }

    [Fact]
    public async Task OtpPasswordReset_EndsExistingSessions()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        var fingerprint = PasswordFingerprint.Compute(user.PasswordHash);
        Assert.True(await env.Sessions.IsValidAsync(user.Id, user.Role, fingerprint));
        await AddOtpAsync(env, user, "123456");

        var (success, _) = await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", "123456", NewPassword);

        Assert.True(success);
        Assert.False(await env.Sessions.IsValidAsync(user.Id, user.Role, fingerprint));
    }

    [Fact]
    public async Task AdminOtpPasswordReset_EndsExistingSessions()
    {
        var env = new Env();
        await env.AddUserAsync(SuperAdminEmail, "Admin");
        var admin = await env.AddUserAsync("ops-admin@example.com", "Admin");
        var fingerprint = PasswordFingerprint.Compute(admin.PasswordHash);
        Assert.True(await env.Sessions.IsValidAsync(admin.Id, admin.Role, fingerprint));
        await AddOtpAsync(env, admin, "654321");

        var (success, _) = await env.Otp().VerifyAndResetAdminPasswordAsync("ops-admin@example.com", "654321", NewPassword);

        Assert.True(success);
        Assert.False(await env.Sessions.IsValidAsync(admin.Id, admin.Role, fingerprint));
    }

    [Fact]
    public async Task SuperAdminRecoveryKeyReset_EndsTheOldSessions()
    {
        var env = new Env();
        var admin = await env.AddUserAsync(SuperAdminEmail, "Admin");
        var fingerprint = PasswordFingerprint.Compute(admin.PasswordHash);
        Assert.True(await env.Sessions.IsValidAsync(admin.Id, admin.Role, fingerprint));

        Assert.True(await env.Users().ResetAdminPasswordAsync(SuperAdminEmail, NewPassword));

        Assert.False(await env.Sessions.IsValidAsync(admin.Id, admin.Role, fingerprint));
        Assert.NotNull((await env.Auth.ValidateAsync(SuperAdminEmail, NewPassword, "Admin")).User);
    }

    [Fact]
    public async Task SessionWithoutAStamp_OrForADeletedUser_IsInvalid()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");

        Assert.False(await env.Sessions.IsValidAsync(user.Id, user.Role, null));
        Assert.False(await env.Sessions.IsValidAsync(user.Id, "Admin", PasswordFingerprint.Compute(user.PasswordHash))); // role mismatch
        Assert.False(await env.Sessions.IsValidAsync(user.Id + 100, user.Role, PasswordFingerprint.Compute(user.PasswordHash)));
    }

    [Fact]
    public async Task AuthFilter_RedirectsAStaleSession_AndLetsAFreshOneThrough()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        var oldStamp = env.SignIn(user);
        var services = new ServiceCollection().AddSingleton<ISessionValidationService>(env.Sessions).BuildServiceProvider();
        env.Http.RequestServices = services;

        var fresh = await RunFilterAsync(env.Http, new RequireAuthAttribute());
        Assert.True(fresh.NextCalled);
        Assert.Null(fresh.Result);

        await env.Auth.ChangePasswordAsync(user.Id, OldPassword, NewPassword); // current session re-stamped
        Assert.True((await RunFilterAsync(env.Http, new RequireAuthAttribute())).NextCalled);

        env.UseNewSession(); // a session opened before the change, still carrying the old stamp
        env.Session.SetUserSession(user.Id, user.Email, user.Role, null, false, oldStamp);
        var stale = await RunFilterAsync(env.Http, new RequireAuthAttribute());
        Assert.False(stale.NextCalled);
        Assert.Equal("/login", Assert.IsType<RedirectResult>(stale.Result).Url);
        Assert.Null(env.Session.GetUserId()); // the stale session was cleared
    }

    private static async Task<(bool NextCalled, IActionResult? Result)> RunFilterAsync(HttpContext http, SessionAuthFilterAttribute filter)
    {
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var nextCalled = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (nextCalled, executing.Result);
    }

    // ── 3. absolute session lifetime ─────────────────────────────────────────

    [Fact]
    public void Session_ExpiresAfterTheAbsoluteLifetime_RegardlessOfActivity()
    {
        var session = new FakeSession();
        session.SetUserSession(1, "a@example.com", "User", null, false, "fp");
        var loggedInAt = DateTimeOffset.UtcNow;

        Assert.False(session.IsPastAbsoluteLifetime(loggedInAt.AddHours(1)));
        Assert.False(session.IsPastAbsoluteLifetime(loggedInAt.AddHours(11)));
        Assert.True(session.IsPastAbsoluteLifetime(loggedInAt.AddHours(13)));
        Assert.Equal(TimeSpan.FromHours(12), MvcApp.Extensions.SessionExtensions.AbsoluteLifetime);
    }

    [Fact]
    public void Session_WithoutALoginStamp_IsTreatedAsExpired()
    {
        Assert.True(new FakeSession().IsPastAbsoluteLifetime(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AuthFilter_RejectsASessionPastItsAbsoluteLifetime()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        env.SignIn(user);
        env.Session.SetString("LoginAt", DateTimeOffset.UtcNow.AddHours(-13).ToUnixTimeSeconds().ToString());
        env.Http.RequestServices = new ServiceCollection().AddSingleton<ISessionValidationService>(env.Sessions).BuildServiceProvider();

        var result = await RunFilterAsync(env.Http, new RequireAuthAttribute());

        Assert.False(result.NextCalled);
        Assert.IsType<RedirectResult>(result.Result);
        Assert.Null(env.Session.GetUserId());
    }

    // ── 4. OTP generation / verification ─────────────────────────────────────

    [Fact]
    public void OtpCodes_AreAlwaysSixDigits_AndNotConstant()
    {
        var codes = Enumerable.Range(0, 3000).Select(_ => OtpService.GenerateCode()).ToList();

        Assert.All(codes, c => Assert.Matches(new Regex("^[0-9]{6}$"), c));
        Assert.True(codes.Distinct().Count() > 2500);
        Assert.Contains(codes, c => c.StartsWith('0')); // leading zeros are kept, not dropped
    }

    private static async Task AddOtpAsync(Env env, User user, string code, DateTime? expiresAt = null, int failedAttempts = 0)
    {
        env.Db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id, OtpCode = BCrypt.Net.BCrypt.HashPassword(code, 4),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(24), FailedAttempts = failedAttempts,
        });
        await env.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Otp_CorrectCode_SetsTheNewPassword_AndCannotBeReused()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        await AddOtpAsync(env, user, "042517");

        var first = await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", " 042517 ", NewPassword);
        var second = await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", "042517", "Another-Pass-789!");

        Assert.True(first.success);
        Assert.False(second.success);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", NewPassword, "Home")).User);
    }

    [Fact]
    public async Task Otp_ExpiredCode_IsRejected()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        await AddOtpAsync(env, user, "111111", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", "111111", NewPassword);

        Assert.False(result.success);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User); // password untouched
    }

    [Fact]
    public async Task Otp_FiveWrongGuesses_BurnTheCode_EvenIfTheNextOneIsRight()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");
        await AddOtpAsync(env, user, "222222");

        for (var i = 0; i < 5; i++)
            Assert.False((await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", "999999", NewPassword)).success);
        var correctAfterwards = await env.Otp().VerifyAndResetPasswordAsync("worker@example.com", "222222", NewPassword);

        Assert.False(correctAfterwards.success);
        Assert.True(env.Db.PasswordResetOtps.Single().IsUsed);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", OldPassword, "Home")).User);
    }

    [Fact]
    public async Task Otp_ForUnknownAccount_GivesTheSameAnswerAsNoActiveCode()
    {
        var env = new Env();
        var user = await env.AddUserAsync("worker@example.com");

        var unknown = await env.Otp().VerifyAndResetPasswordAsync("nobody@example.com", "123456", NewPassword);
        var noCode = await env.Otp().VerifyAndResetPasswordAsync(user.Email, "123456", NewPassword);

        Assert.False(unknown.success);
        Assert.Equal(noCode.message, unknown.message);
    }

    // ── 5. one-time tokens ───────────────────────────────────────────────────

    [Fact]
    public void SecureTokens_AreLongRandomHex_AndUnique()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => SecureToken.Create()).ToList();

        Assert.All(tokens, t => Assert.Matches(new Regex("^[0-9a-f]{64}$"), t));
        Assert.Equal(1000, tokens.Distinct().Count());
    }

    [Fact]
    public void PasswordSetupToken_WorksOnce_AndRejectsGuesses()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var user = new User { Id = 7, Email = "new@example.com" };

        var token = cache.BeginPasswordSetup(user);

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), token);
        Assert.False(cache.TryCompletePasswordSetup("0123456789abcdef", out _, out _));
        Assert.True(cache.TryCompletePasswordSetup(token, out var userId, out var email));
        Assert.Equal(7, userId);
        Assert.Equal("new@example.com", email);
        Assert.False(cache.TryCompletePasswordSetup(token, out _, out _)); // single use
        Assert.False(cache.TryCompletePasswordSetup(null, out _, out _));
    }

    [Fact]
    public void SessionRotationToken_EstablishesTheSession_OnceOnly()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var http = new DefaultHttpContext();
        http.Features.Set<ISessionFeature>(new FakeSessionFeature { Session = new FakeSession() });

        var token = http.BeginSessionRotation(cache, 5, "worker@example.com", "User", "Worker", false, "stamp-1");

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), token);
        Assert.False(http.CompleteSessionRotation(cache, "not-a-real-token"));
        Assert.Null(http.Session.GetUserId());
        Assert.True(http.CompleteSessionRotation(cache, token));
        Assert.Equal(5, http.Session.GetUserId());
        Assert.Equal("User", http.Session.GetRole());
        Assert.Equal("stamp-1", http.Session.GetPasswordFingerprint());
        Assert.False(http.Session.IsPastAbsoluteLifetime(DateTimeOffset.UtcNow));
        Assert.False(http.CompleteSessionRotation(cache, token)); // single use
    }

    // ── 6. password-change flows ─────────────────────────────────────────────

    [Fact]
    public async Task SetPassword_OnlyWorksForAccountsStillOnATemporaryPassword()
    {
        var env = new Env();
        var settled = await env.AddUserAsync("settled@example.com");
        var pending = await env.AddUserAsync("pending@example.com", mustChange: true);

        Assert.False(await env.Auth.SetPasswordAsync(settled.Id, NewPassword));
        Assert.NotNull((await env.Auth.ValidateAsync("settled@example.com", OldPassword, "Home")).User);

        Assert.True(await env.Auth.SetPasswordAsync(pending.Id, NewPassword));
        Assert.NotNull((await env.Auth.ValidateAsync("pending@example.com", NewPassword, "Home")).User);
        Assert.False(await env.Auth.SetPasswordAsync(pending.Id, "Third-Password-000!")); // token replay
    }

    [Fact]
    public async Task AdminCanStillResetANonAdminUsersPassword_AndGetsNoSelfEscalation()
    {
        var env = new Env();
        var normalAdmin = await env.AddUserAsync("ops-admin@example.com", "Admin");
        await env.AddUserAsync(SuperAdminEmail, "Admin");
        var worker = await env.AddUserAsync("worker@example.com");

        var reset = await env.Users().UpdateAsync(worker.Id,
            new EditUserViewModel { Email = worker.Email, Phone = worker.Phone, Role = "User", Password = NewPassword, ConfirmPassword = NewPassword },
            "ops-admin@example.com");
        Assert.Null(reset.error);
        Assert.NotNull((await env.Auth.ValidateAsync("worker@example.com", NewPassword, "Home")).User);

        // ...but cannot reach the Super Admin's account, nor promote anyone to Admin.
        var superAdmin = env.Db.Users.Single(u => u.Email == SuperAdminEmail);
        var touchSuper = await env.Users().UpdateAsync(superAdmin.Id,
            new EditUserViewModel { Email = superAdmin.Email, Phone = superAdmin.Phone, Role = "Admin", Password = NewPassword, ConfirmPassword = NewPassword },
            normalAdmin.Email);
        Assert.Null(touchSuper.user);
        Assert.NotNull((await env.Auth.ValidateAsync(SuperAdminEmail, OldPassword, "Admin")).User);

        var promote = await env.Users().UpdateAsync(worker.Id,
            new EditUserViewModel { Email = worker.Email, Phone = worker.Phone, Role = "Admin" }, normalAdmin.Email);
        Assert.Equal("role-forbidden", promote.error);
    }
}
