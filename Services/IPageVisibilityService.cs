namespace MvcApp.Services;

public interface IPageVisibilityService
{
    /// <summary>Keys of the pages hidden from the User interface (cached).</summary>
    Task<HashSet<string>> GetHiddenAsync();

    /// <summary>Every page key → is it hidden.</summary>
    Task<Dictionary<string, bool>> GetAllAsync();

    /// <summary>Saves which pages are hidden. At least one page must stay visible, otherwise
    /// <see cref="InvalidOperationException"/> is thrown and nothing is saved.</summary>
    Task SaveAsync(Dictionary<string, bool> hidden, string? adminName);
}
