using MvcApp.Models;

namespace MvcApp.Services;

public interface IStoreService
{
    Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName);
}
