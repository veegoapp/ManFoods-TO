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

    public async Task<bool> IsValidAsync(int userId, string role)
    {
        var key = CacheKey(userId);
        if (!_cache.TryGetValue(key, out string? currentRole))
        {
            // Null means "no such user" (deleted) — Role itself is a required,
            // non-null column, so a null projection can only mean a missing row.
            currentRole = await _db.Users.Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync();
            _cache.Set(key, currentRole, CacheDuration);
        }
        return currentRole != null && currentRole == role;
    }

    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));
}
