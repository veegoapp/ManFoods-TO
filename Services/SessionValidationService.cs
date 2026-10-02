using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;

namespace MvcApp.Services;

/// <summary>
/// Backed by the same IMemoryCache infrastructure DashboardService already uses
/// for KPI caching — a short per-user cache entry means a page's burst of AJAX
/// calls (Dashboard alone fires well over a dozen) costs at most one lightweight
/// "SELECT role" per user every 30 seconds, not one per request, while still
/// catching a deleted/role-changed user within a bounded, short window instead
/// of the full session lifetime. UserService proactively calls
/// <see cref="Invalidate"/> right after an admin edits/deletes a user, so the
/// common case (an admin action, not a passive expiry) takes effect on the very
/// next request rather than waiting out the cache window.
/// </summary>
public class SessionValidationService : ISessionValidationService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public SessionValidationService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static string CacheKey(int userId) => $"session_validate_{userId}";

    private sealed record SessionIdentity(string Role, string PasswordFingerprint);

    public async Task<bool> IsValidAsync(int userId, string role, string? passwordFingerprint)
    {
        var key = CacheKey(userId);
        if (!_cache.TryGetValue(key, out SessionIdentity? current))
        {
            // Null means "no such user" (deleted) — Role itself is a required,
            // non-null column, so a null projection can only mean a missing row.
            var row = await _db.Users.Where(u => u.Id == userId).Select(u => new { u.Role, u.PasswordHash }).FirstOrDefaultAsync();
            current = row == null ? null : new SessionIdentity(row.Role, PasswordFingerprint.Compute(row.PasswordHash));
            _cache.Set(key, current, CacheDuration);
        }
        return current != null && current.Role == role && PasswordFingerprint.Matches(current.PasswordFingerprint, passwordFingerprint);
    }

    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));
}
