namespace MvcApp.Filters;

/// <summary>
/// Declares which User-interface pages (UserPages keys) an API or download endpoint serves. A signed-in
/// non-Admin user is let through only while at least one of those pages is visible to their role in
/// Settings → Pages; when every one of them is hidden from that role the request is refused with 403 — so
/// hiding a page also closes the data behind it, not just its link. Endpoints shared by several pages list
/// all of them. An action-level attribute replaces the controller-level one. Admin is never affected, and
/// endpoints without this attribute (and the shared filter-dropdown lookups) are never blocked.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresAnyPageAttribute : Attribute
{
    public IReadOnlyList<string> PageKeys { get; }
    public RequiresAnyPageAttribute(params string[] pageKeys) => PageKeys = pageKeys;
}
