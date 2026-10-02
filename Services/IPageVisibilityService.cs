namespace MvcApp.Services;

public interface IPageVisibilityService
{
    /// <summary>Keys of the pages hidden from <paramref name="role"/> in the User interface (cached per role).</summary>
    Task<HashSet<string>> GetHiddenAsync(string role);

    /// <summary>Every page key → role → is it hidden, for every page and every role in <see cref="UserPages.Roles"/>.</summary>
    Task<Dictionary<string, Dictionary<string, bool>>> GetAllAsync();

    /// <summary>Saves which pages are hidden from which roles (page key → role → hidden; anything not sent keeps its
    /// current value; unknown pages and roles are ignored). Every role must keep at least one visible page,
    /// otherwise <see cref="InvalidOperationException"/> is thrown and nothing is saved.</summary>
    Task SaveAsync(Dictionary<string, Dictionary<string, bool>> hidden, string? adminName);
}
