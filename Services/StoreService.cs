using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;

    public StoreService(AppDbContext db) => _db = db;

    public async Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);

        // Role system simplified to Admin/User — no more per-store restriction.
        return await q.OrderBy(s => s.StoreName).ToListAsync();
    }
}
