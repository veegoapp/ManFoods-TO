using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;

    public StoreService(AppDbContext db, IStoreAccessService storeAccess)
    {
        _db = db;
        _storeAccess = storeAccess;
    }

    public async Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);

        // "assignedName" here is actually the logged-in user's email (see
        // HttpContext.Session.GetEmail() at call sites). Restriction is
        // resolved centrally by IStoreAccessService, always against the
        // latest uploaded period — so a rotation applies to every historical
        // month a restricted user can browse, not just the current one.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));

        return await q.OrderBy(s => s.StoreName).ToListAsync();
    }
}
