namespace MvcApp.Services;

/// <summary>
/// Per-request holder for the access area the current endpoint serves. Set by
/// the AccessArea action filter from an [AccessArea] attribute, and read by
/// StoreAccessService to decide whether to widen a restricted role's view for
/// that area. Null (no attribute) is treated as a shared/neutral area.
/// </summary>
public interface IAccessAreaContext
{
    string? Area { get; set; }
}

public sealed class AccessAreaContext : IAccessAreaContext
{
    public string? Area { get; set; }
}
