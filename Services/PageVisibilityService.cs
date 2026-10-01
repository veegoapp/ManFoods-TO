using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class PageVisibilityService : IPageVisibilityService
{
    private const string CacheKey = "page-visibility:hidden";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public PageVisibilityService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<HashSet<string>> GetHiddenAsync()
    {
        if (_cache.TryGetValue(CacheKey, out HashSet<string>? cached) && cached != null) return cached;
        var rows = await _db.PageVisibilities.AsNoTracking().Where(p => p.IsHidden).Select(p => p.PageKey).ToListAsync();
        var set = rows.Where(UserPages.IsKnown).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cache.Set(CacheKey, set, CacheTtl);
        return set;
    }

    public async Task<Dictionary<string, bool>> GetAllAsync()
    {
        var hidden = await GetHiddenAsync();
        return UserPages.All.ToDictionary(p => p.Key, p => hidden.Contains(p.Key));
    }

    public async Task SaveAsync(Dictionary<string, bool> hidden, string? adminName)
    {
        // The result for every known page: what was sent, else what it was before.
        var current = await GetAllAsync();
        foreach (var kv in hidden)
            if (UserPages.IsKnown(kv.Key)) current[kv.Key] = kv.Value;
        if (current.Values.All(h => h))
            throw new InvalidOperationException("At least one page must stay visible.");

        var now = DateTime.UtcNow;
        var existing = (await _db.PageVisibilities.ToListAsync()).ToDictionary(r => r.PageKey, StringComparer.OrdinalIgnoreCase);
        foreach (var page in UserPages.All)
        {
            var isHidden = current[page.Key];
            if (existing.TryGetValue(page.Key, out var row)) { row.IsHidden = isHidden; row.UpdatedByName = adminName; row.UpdatedAt = now; }
            else if (isHidden) _db.PageVisibilities.Add(new PageVisibility { PageKey = page.Key, IsHidden = true, UpdatedByName = adminName, UpdatedAt = now });
        }
        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }
}
