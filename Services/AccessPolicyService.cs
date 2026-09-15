using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AccessPolicyService : IAccessPolicyService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    private const string CacheKey = "access-policy:map";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public AccessPolicyService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
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

    public async Task<bool> IsRestrictedAsync(string areaKey)
    {
        if (string.IsNullOrEmpty(areaKey)) return true;
        var map = await LoadAsync();
        // Missing row → restricted (safe default).
        return !map.TryGetValue(areaKey, out var restricted) || restricted;
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
            a => !map.TryGetValue(a, out var restricted) || restricted,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(Dictionary<string, bool> settings, string? adminName)
    {
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
    }
}
