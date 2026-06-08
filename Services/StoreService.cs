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

        if (role == "Operation_Manager" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(s => s.OperationManager == assignedName);
        else if (role == "Operation_Consultant" && !string.IsNullOrEmpty(assignedName))
            q = q.Where(s => s.OperationConsultant == assignedName);

        return await q.OrderBy(s => s.StoreName).ToListAsync();
    }
}
