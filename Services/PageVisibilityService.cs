using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class PageVisibilityService : IPageVisibilityService
{
    private const string CacheKeyPrefix = "page-visibility:hidden:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IReadOnlyList<UserPages.Page> _pages;

    // `pages` lets tests supply pages (for example one that is under review); the application uses UserPages.All.
    public PageVisibilityService(AppDbContext db, IMemoryCache cache, IReadOnlyList<UserPages.Page>? pages = null)
    {
        _db = db;
        _cache = cache;
        _pages = pages ?? UserPages.All;
    }

    public async Task<HashSet<string>> GetHiddenAsync(string role)
    {
        var key = CacheKeyPrefix + role;
        if (_cache.TryGetValue(key, out HashSet<string>? cached) && cached != null) return cached;

        // Start from each page's default for this role, then apply what an Admin saved.
        var hidden = _pages.Where(p => UserPages.HiddenByDefault(p, role)).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await _db.PageVisibilities.AsNoTracking().Where(p => p.Role == role).ToListAsync();
        foreach (var row in rows.Where(r => _pages.Any(p => p.Key == r.PageKey)))
        {
            if (row.IsHidden) hidden.Add(row.PageKey);
            else hidden.Remove(row.PageKey);
        }
        _cache.Set(key, hidden, CacheTtl);
        return hidden;
    }

    public async Task<Dictionary<string, Dictionary<string, bool>>> GetAllAsync()
    {
        var result = _pages.ToDictionary(p => p.Key, _ => new Dictionary<string, bool>());
        foreach (var role in UserPages.Roles)
        {
            var hidden = await GetHiddenAsync(role);
            foreach (var page in _pages) result[page.Key][role] = hidden.Contains(page.Key);
        }
        return result;
    }

    public async Task SaveAsync(Dictionary<string, Dictionary<string, bool>> hidden, string? adminName)
    {
        // The result for every page and role: what was sent, else what it was before.
        var current = await GetAllAsync();
        foreach (var (pageKey, byRole) in hidden)
        {
            if (!current.TryGetValue(pageKey, out var row)) continue;
            foreach (var (role, isHidden) in byRole)
                if (UserPages.IsKnownRole(role)) row[role] = isHidden;
        }
        foreach (var role in UserPages.Roles)
            if (_pages.All(p => current[p.Key][role]))
                throw new InvalidOperationException("At least one page must stay visible for every role.");

        var now = DateTime.UtcNow;
        var existing = (await _db.PageVisibilities.ToListAsync()).ToDictionary(r => (r.PageKey.ToLowerInvariant(), r.Role));
        foreach (var page in _pages)
        {
            foreach (var role in UserPages.Roles)
            {
                var isHidden = current[page.Key][role];
                if (existing.TryGetValue((page.Key.ToLowerInvariant(), role), out var row)) { row.IsHidden = isHidden; row.UpdatedByName = adminName; row.UpdatedAt = now; }
                // Only a value that differs from the page's default needs a row.
                else if (isHidden != UserPages.HiddenByDefault(page, role))
                    _db.PageVisibilities.Add(new PageVisibility { PageKey = page.Key, Role = role, IsHidden = isHidden, UpdatedByName = adminName, UpdatedAt = now });
            }
        }
        await _db.SaveChangesAsync();
        foreach (var role in UserPages.Roles) _cache.Remove(CacheKeyPrefix + role);
    }
}
