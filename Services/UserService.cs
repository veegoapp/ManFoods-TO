using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) => _db = db;

    private static UserViewModel ToVm(User u) => new()
    {
        Id = u.Id, Email = u.Email, Role = u.Role,
        AssignedName = u.AssignedName, CreatedAt = u.CreatedAt
    };

    public async Task<List<UserViewModel>> GetAllAsync() =>
        await _db.Users.OrderBy(u => u.CreatedAt).Select(u => new UserViewModel
        {
            Id = u.Id, Email = u.Email, Role = u.Role,
            AssignedName = u.AssignedName, CreatedAt = u.CreatedAt
        }).ToListAsync();

    public async Task<UserViewModel?> GetByIdAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        return u == null ? null : ToVm(u);
    }

    public async Task<UserViewModel> CreateAsync(CreateUserViewModel vm)
    {
        var user = new User
        {
            Email = vm.Email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            Role = vm.Role,
            AssignedName = string.IsNullOrEmpty(vm.AssignedName) ? null : vm.AssignedName
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return ToVm(user);
    }

    public async Task<UserViewModel?> UpdateAsync(int id, EditUserViewModel vm)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return null;
        user.Email = vm.Email.ToLower();
        user.Role = vm.Role;
        user.AssignedName = string.IsNullOrEmpty(vm.AssignedName) ? null : vm.AssignedName;
        if (!string.IsNullOrEmpty(vm.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password);
        await _db.SaveChangesAsync();
        return ToVm(user);
    }

    public async Task DeleteAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null) { _db.Users.Remove(user); await _db.SaveChangesAsync(); }
    }

    public async Task<(List<string>, List<string>)> GetAssignableNamesAsync()
    {
        var stores = await _db.StoreReferences
            .Select(s => new { s.OperationManager, s.OperationConsultant })
            .ToListAsync();
        var managers = stores.Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .Select(s => s.OperationManager!).Distinct().OrderBy(x => x).ToList();
        var consultants = stores.Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .Select(s => s.OperationConsultant!).Distinct().OrderBy(x => x).ToList();
        return (managers, consultants);
    }
}
