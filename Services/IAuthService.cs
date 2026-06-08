using MvcApp.Models;

namespace MvcApp.Services;

public interface IAuthService
{
    Task<User?> ValidateAsync(string email, string password);
}
