using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace MvcApp.Extensions;

public static class SessionExtensions
{
    /// <summary>Name of the session cookie — shared with Program.cs's AddSession
    /// config so both places (and the fixation-mitigation logic below, which
    /// has to delete this exact cookie) stay in sync from one source.</summary>
    public const string SessionCookieName = "wicrewsession";

    /// <summary>Absolute maximum session age, counted from login and never extended by
    /// activity (the 1-hour idle timeout in Program.cs still applies on top). 12 hours
    /// covers a full working day in one sign-in, while a session cookie that is kept
    /// alive by regular use can no longer live forever.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public static void SetUserSession(this ISession session, int userId, string email, string role, string? assignedName, bool mustChangePassword, string passwordFingerprint)
    {
        session.SetInt32("UserId", userId);
        session.SetString("Email", email);
        session.SetString("Role", role);
        session.SetString("AssignedName", assignedName ?? "");
        session.SetInt32("MustChangePassword", mustChangePassword ? 1 : 0);
        session.SetString("PasswordFingerprint", passwordFingerprint);
        session.SetString("LoginAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The PasswordFingerprint of the password this session was opened (or last
    /// re-stamped, after the owner's own password change) with.</summary>
    public static string? GetPasswordFingerprint(this ISession session) => session.GetString("PasswordFingerprint");
    public static void SetPasswordFingerprint(this ISession session, string fingerprint) => session.SetString("PasswordFingerprint", fingerprint);

    /// <summary>True once the session is older than <see cref="AbsoluteLifetime"/>; a session
    /// with no (or an unreadable) login stamp is treated as expired.</summary>
    public static bool IsPastAbsoluteLifetime(this ISession session, DateTimeOffset now)
    {
        if (!long.TryParse(session.GetString("LoginAt"), NumberStyles.None, CultureInfo.InvariantCulture, out var loginAtSeconds)) return true;
        return now - DateTimeOffset.FromUnixTimeSeconds(loginAtSeconds) > AbsoluteLifetime;
    }

    public static int? GetUserId(this ISession session) => session.GetInt32("UserId");
    public static string GetEmail(this ISession session) => session.GetString("Email") ?? "";
    public static string GetRole(this ISession session) => session.GetString("Role") ?? "";
    public static string? GetAssignedName(this ISession session)
    {
        var v = session.GetString("AssignedName");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static bool IsAdmin(this ISession session) => session.GetRole() == "Admin";

    /// <summary>True while the logged-in account's password is still a
    /// system-generated temporary one — set at login from User.MustChangePassword,
    /// and cleared in-session (via SetMustChangePassword(false)) the moment the
    /// account owner successfully sets their own password, so the forced
    /// redirect (SessionAuthFilterAttribute) stops immediately without
    /// requiring a fresh login.</summary>
    public static bool GetMustChangePassword(this ISession session) => session.GetInt32("MustChangePassword") == 1;
    public static void SetMustChangePassword(this ISession session, bool value) => session.SetInt32("MustChangePassword", value ? 1 : 0);

    private record struct PendingLogin(int UserId, string Email, string Role, string? AssignedName, bool MustChangePassword, string PasswordFingerprint);
    private const string PendingLoginPrefix = "pending-login:";

    /// <summary>
    /// Session-fixation mitigation: ASP.NET Core's session middleware has no
    /// public "regenerate session ID" call — it decides the session key once,
    /// from the incoming request's cookie, before the controller ever runs.
    /// So instead of writing the freshly-authenticated identity into whatever
    /// session (possibly attacker-fixed) the request arrived with, this
    /// discards that session entirely, stashes the identity behind a random
    /// one-time token in the already-registered IMemoryCache, and hands the
    /// token back so the caller can redirect. The follow-up request then
    /// arrives with no session cookie at all, which forces the middleware
    /// down its normal "no cookie present" path — the same path it always
    /// uses to issue a brand-new, unguessable session ID — and
    /// CompleteSessionRotation below writes the identity into that new
    /// session. Returns the one-time token to redirect with.
    /// </summary>
    public static string BeginSessionRotation(this HttpContext context, IMemoryCache cache, int userId, string email, string role, string? assignedName, bool mustChangePassword, string passwordFingerprint)
    {
        // Deliberately does NOT touch context.Session (no Clear()/Set* call):
        // doing so would mark the old session dirty and could race the
        // explicit cookie deletion below against the session middleware's own
        // end-of-request cookie-write for that (soon-abandoned) session. Since
        // the old session's identity is never written to and its cookie is
        // deleted here, it simply becomes orphaned — harmless whether or not
        // it was attacker-fixed.
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });

        var token = SecureToken.Create();
        cache.Set(PendingLoginPrefix + token, new PendingLogin(userId, email, role, assignedName, mustChangePassword, passwordFingerprint), TimeSpan.FromSeconds(60));
        return token;
    }

    /// <summary>Redeems a one-time token from BeginSessionRotation into the
    /// current (now-guaranteed-fresh) session. Returns false if the token is
    /// missing, expired, or already used.</summary>
    public static bool CompleteSessionRotation(this HttpContext context, IMemoryCache cache, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var key = PendingLoginPrefix + token;
        if (!cache.TryGetValue(key, out PendingLogin pending)) return false;
        cache.Remove(key);
        context.Session.SetUserSession(pending.UserId, pending.Email, pending.Role, pending.AssignedName, pending.MustChangePassword, pending.PasswordFingerprint);
        return true;
    }
}
