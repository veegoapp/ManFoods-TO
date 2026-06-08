using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IUserService
{
    Task<List<UserViewModel>> GetAllAsync();
    Task<UserViewModel?> GetByIdAsync(int id);
    Task<UserViewModel> CreateAsync(CreateUserViewModel vm);
    Task<UserViewModel?> UpdateAsync(int id, EditUserViewModel vm);
    Task DeleteAsync(int id);
    Task<(List<string> managers, List<string> consultants)> GetAssignableNamesAsync();
}
