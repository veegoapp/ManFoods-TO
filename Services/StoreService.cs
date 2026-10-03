using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    // Results keyed by (month, year, role, user) — bounded, see FilterResultCache. UploadService bumps its
    // data version whenever the store reference changes, so uploads show up immediately.
    private readonly FilterResultCache _filterCache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public StoreService(AppDbContext db, IStoreAccessService storeAccess, FilterResultCache? filterCache = null)
    {
        _db = db;
        _storeAccess = storeAccess;
        _filterCache = filterCache ?? new FilterResultCache();
    }

    public async Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName)
    {
        // Restriction is resolved centrally by IStoreAccessService (area-aware: a restricted role may be
        // widened to all stores for some endpoints), so resolve it first and key the cache on the RESULT —
        // two callers share an entry only when they may see the same stores.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        // Called on page opens and every period change; with no month/year it reads the whole table.
        var cacheKey = _filterCache.KeyFor("stores", month, year, role, assignedName, AccessScopeKey.Of(accessible));
        if (_filterCache.TryGet(cacheKey, out List<StoreReference>? cached))
            return new List<StoreReference>(cached!);

        var q = _db.StoreReferences.AsNoTracking();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));

        var stores = await q.OrderBy(s => s.StoreName).ToListAsync();
        _filterCache.Set(cacheKey, stores, CacheDuration, stores.Count);
        return new List<StoreReference>(stores);
    }
}
