using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Single source of truth for "which stores can this user see." Backed
/// entirely by the latest uploaded Store Reference period — there is no
/// separate user-store assignment table, so a monthly re-upload changes
/// access automatically.
/// </summary>
public interface IStoreAccessService
{
    /// <summary>True for roles that are restricted to specific stores (as opposed to Admin/User, which see everything).</summary>
    bool IsRestrictedRole(string role);

    /// <summary>Role keys currently mapped to a StoreReference email column, in a stable display order.</summary>
    IReadOnlyList<string> RestrictedRoles { get; }

    /// <summary>
    /// Store names the given role/email currently has access to, based on the
    /// latest (Month, Year) present in StoreReference. Null means unrestricted
    /// (Admin/User or any role not in the access map).
    /// </summary>
    Task<List<string>?> GetAccessibleStoreNamesAsync(string role, string? email);

    /// <summary>The email value on a StoreReference row for the given restricted role (e.g. for upload validation).</summary>
    string GetEmailForRole(StoreReference store, string role);

    /// <summary>The display-name value on a StoreReference row for the given restricted role (e.g. for the Action Plan Role settings page).</summary>
    string GetNameForRole(StoreReference store, string role);

    /// <summary>Store names the role/email owns via the email match, never widened
    /// by the per-area access configuration — the basis for write permissions.
    /// Null = unrestricted (Admin/User).</summary>
    Task<List<string>?> GetOwnStoreNamesAsync(string role, string? email);

    /// <summary>True if the given role/email can see the given store — unrestricted
    /// roles (Admin/User) always true; restricted roles checked against the same
    /// latest-period access list as <see cref="GetAccessibleStoreNamesAsync"/>.</summary>
    Task<bool> CanAccessStoreAsync(string role, string? email, string storeName);

    /// <summary>Write-permission check — whether the role/email may manage (write to)
    /// the store. Ignores the access-area widening, so opening a page's view never
    /// grants write access to stores the user doesn't own.</summary>
    Task<bool> CanManageStoreAsync(string role, string? email, string storeName);

    /// <summary>Resolves who is currently responsible for a store: the Head Manager
    /// if one is assigned on the latest StoreReference data, otherwise the Operation
    /// Consultant. Computed live on every call — never persisted — so a Store
    /// Reference change takes effect immediately. Null if the store isn't found.</summary>
    Task<ResponsibleParty?> GetResponsiblePartyAsync(string storeName);

    /// <summary>Pure "Head Manager if assigned, else Operation Consultant" rule
    /// applied to an already-loaded StoreReference row — shared by
    /// <see cref="GetResponsiblePartyAsync"/> and by callers batch-resolving
    /// responsibility for many stores at once without a query per store.</summary>
    ResponsibleParty ResolveResponsible(StoreReference reference);
}
