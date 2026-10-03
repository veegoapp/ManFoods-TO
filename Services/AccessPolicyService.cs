using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AccessPolicyService : IAccessPolicyService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly FilterResultCache _filterCache;

    private const string CacheKey = "access-policy:map";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public AccessPolicyService(AppDbContext db, IMemoryCache cache, FilterResultCache? filterCache = null)
    {
        _db = db;
        _cache = cache;
        _filterCache = filterCache ?? new FilterResultCache();
    }

    private async Task<Dictionary<string, bool>> LoadAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, bool>? cached) && cached != null)
            return cached;

        var rows = await _db.PageAccessConfigs.AsNoTracking().ToListAsync();
        var map = rows.ToDictionary(r => r.AreaKey, r => r.IsRestricted, StringComparer.OrdinalIgnoreCase);
        _cache.Set(CacheKey, map, CacheTtl);
        return map;
    }

    private static bool Resolve(Dictionary<string, bool> map, string areaKey)
    {
        if (map.TryGetValue(areaKey, out var restricted)) return restricted;
        if (AccessAreas.FormerlyShared.TryGetValue(areaKey, out var parent) && map.TryGetValue(parent, out var parentRestricted)) return parentRestricted;
        return true;
    }

    public async Task<bool> IsRestrictedAsync(string areaKey)
    {
        if (string.IsNullOrEmpty(areaKey)) return true;
        var map = await LoadAsync();
        // Missing row → restricted (safe default), unless the area was split off another one: then it follows that area.
        return Resolve(map, areaKey);
    }

    public async Task<bool> AnyOpenAsync()
    {
        var map = await LoadAsync();
        return map.Values.Any(restricted => !restricted);
    }

    public async Task<Dictionary<string, bool>> GetAllAsync()
    {
        var map = await LoadAsync();
        return AccessAreas.All.ToDictionary(
            a => a,
            a => Resolve(map, a),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> SaveAsync(Dictionary<string, bool> settings, string? adminName)
    {
        var before = await GetAllAsync();
        var changes = new List<string>();
        foreach (var area in AccessAreas.All)
            if (settings.TryGetValue(area, out var wanted) && before[area] != wanted)
                changes.Add($"{area}: {Scope(before[area])} → {Scope(wanted)}");
        var now = DateTime.UtcNow;
        var existing = await _db.PageAccessConfigs.ToListAsync();
        var byKey = existing.ToDictionary(r => r.AreaKey, StringComparer.OrdinalIgnoreCase);

        foreach (var area in AccessAreas.All)
        {
            if (!settings.TryGetValue(area, out var isRestricted)) continue;
            if (byKey.TryGetValue(area, out var row))
            {
                row.IsRestricted = isRestricted;
                row.UpdatedByName = adminName;
                row.UpdatedAt = now;
            }
            else
            {
                _db.PageAccessConfigs.Add(new PageAccessConfig
                {
                    AreaKey = area, IsRestricted = isRestricted, UpdatedByName = adminName, UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
        // Changing which areas are open changes which stores a restricted role sees, so every cached filter
        // result (computed under the old policy) is dropped now rather than served until its TTL ends.
        _filterCache.InvalidateAll();
        return changes;
    }

    private static string Scope(bool restricted) => restricted ? "own stores" : "all stores";
}
