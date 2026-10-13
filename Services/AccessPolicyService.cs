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

    private static bool Resolve(Dictionary<string, bool> map, string areaKey)
    {
        if (map.TryGetValue(areaKey, out var restricted)) return restricted;
        if (AccessAreas.IsTrainerKey(areaKey)) return true; // a Trainer area nobody saved yet: own stores
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
        // Only the regular column: the Trainer rows share the table but must never widen the other roles' dropdowns.
        return map.Any(kv => !AccessAreas.IsTrainerKey(kv.Key) && !kv.Value);
    }

    public async Task<Dictionary<string, bool>> GetAllAsync()
    {
        var map = await LoadAsync();
        return AccessAreas.All.ToDictionary(
            a => a,
            a => Resolve(map, a),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, bool>> GetAllForTrainerAsync()
    {
        var map = await LoadAsync();
        return AccessAreas.All.ToDictionary(
            a => a,
            a => Resolve(map, AccessAreas.TrainerKey(a)),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<List<string>> SaveAsync(Dictionary<string, bool> settings, string? adminName) =>
        SaveCoreAsync(settings, adminName, trainer: false);

    public Task<List<string>> SaveForTrainerAsync(Dictionary<string, bool> settings, string? adminName) =>
        SaveCoreAsync(settings, adminName, trainer: true);

    private async Task<List<string>> SaveCoreAsync(Dictionary<string, bool> settings, string? adminName, bool trainer)
    {
        string KeyOf(string area) => trainer ? AccessAreas.TrainerKey(area) : area;

        var before = trainer ? await GetAllForTrainerAsync() : await GetAllAsync();
        var changes = new List<string>();
        foreach (var area in AccessAreas.All)
            if (settings.TryGetValue(area, out var wanted) && before[area] != wanted)
                changes.Add($"{(trainer ? "trainer " : "")}{area}: {Scope(before[area])} → {Scope(wanted)}");
        var now = DateTime.UtcNow;
        var existing = await _db.PageAccessConfigs.ToListAsync();
        var byKey = existing.ToDictionary(r => r.AreaKey, StringComparer.OrdinalIgnoreCase);

        foreach (var area in AccessAreas.All)
        {
            if (!settings.TryGetValue(area, out var isRestricted)) continue;
            var key = KeyOf(area);
            if (byKey.TryGetValue(key, out var row))
            {
                row.IsRestricted = isRestricted;
                row.UpdatedByName = adminName;
                row.UpdatedAt = now;
            }
            else
            {
                _db.PageAccessConfigs.Add(new PageAccessConfig
                {
                    AreaKey = key, IsRestricted = isRestricted, UpdatedByName = adminName, UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync();
        _cache.Remove(CacheKey);
        return changes;
    }

    private static string Scope(bool restricted) => restricted ? "own stores" : "all stores";
}
