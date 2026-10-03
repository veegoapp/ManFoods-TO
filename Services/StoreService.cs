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
        // Called on page opens and every period change; with no month/year it reads the whole table.
        var cacheKey = FilterResultCache.BuildKey("stores", month, year, role, assignedName);
        if (_filterCache.TryGet(cacheKey, out List<StoreReference>? cached))
            return new List<StoreReference>(cached!);

        var q = _db.StoreReferences.AsNoTracking();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);

        // "assignedName" here is actually the logged-in user's email (see
        // HttpContext.Session.GetEmail() at call sites). Restriction is
        // resolved centrally by IStoreAccessService, always against the
        // latest uploaded period — so a rotation applies to every historical
        // month a restricted user can browse, not just the current one.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));

        var stores = await q.OrderBy(s => s.StoreName).ToListAsync();
        _filterCache.Set(cacheKey, stores, CacheDuration, stores.Count);
        return new List<StoreReference>(stores);
    }
}
