using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class StoreAccessService : IStoreAccessService
{
    private readonly AppDbContext _db;
    // Optional so the service can still be constructed as `new StoreAccessService(db)`
    // in tests — when absent, every area is treated as restricted (the original
    // behaviour). In the app both are injected.
    private readonly IAccessPolicyService? _policy;
    private readonly IAccessAreaContext? _areaContext;

    public StoreAccessService(AppDbContext db, IAccessPolicyService? policy = null, IAccessAreaContext? areaContext = null)
    {
        _db = db;
        _policy = policy;
        _areaContext = areaContext;
    }

    // The one place that knows which StoreReference email column each
    // restricted role is matched against. Adding a future role (once its
    // column exists on StoreReference) is a single line here — no other
    // file needs to change.
    private static readonly Dictionary<string, Expression<Func<StoreReference, string>>> RoleEmailColumns = new()
    {
        ["Operation_Manager"] = s => s.OperationManagerEmail,
        ["Operation_Consultant"] = s => s.OperationConsultantEmail,
        ["Head_Manager"] = s => s.HeadManagerEmail,
        ["Senior_Operation_Consultant"] = s => s.SeniorOperationConsultantEmail,
        ["Operation_Director"] = s => s.OperationDirectorEmail,
    };

    public IReadOnlyList<string> RestrictedRoles { get; } = RoleEmailColumns.Keys.ToList();

    // Mirrors RoleEmailColumns but for each role's display-name column — used
    // only for display purposes (e.g. the Action Plan Role settings page),
    // never for access decisions.
    private static readonly Dictionary<string, Expression<Func<StoreReference, string>>> RoleNameColumns = new()
    {
        ["Operation_Manager"] = s => s.OperationManager,
        ["Operation_Consultant"] = s => s.OperationConsultant,
        ["Head_Manager"] = s => s.HeadManager,
        ["Senior_Operation_Consultant"] = s => s.SeniorOperationConsultant,
        ["Operation_Director"] = s => s.OperationDirector,
    };

    // Roles with full, unrestricted access — everything else (including a
    // role string that isn't recognized at all, e.g. a Bulk Upload typo) is
    // treated as restricted and gets no store access.
    private static readonly HashSet<string> UnrestrictedRoles = new() { "Admin", "User" };

    public bool IsRestrictedRole(string role) => !UnrestrictedRoles.Contains(role);

    public string GetEmailForRole(StoreReference store, string role) =>
        RoleEmailColumns.TryGetValue(role, out var column) ? column.Compile()(store) : "";

    public string GetNameForRole(StoreReference store, string role) =>
        RoleNameColumns.TryGetValue(role, out var column) ? column.Compile()(store) : "";

    /// <summary>Area-aware read access. Admin/User are always unrestricted. For a
    /// restricted role, whether the view is widened to all stores depends on the
    /// current endpoint's access area (set by the AccessArea filter) and the
    /// admin's per-area configuration: an explicitly opened area returns null
    /// (full access), the shared dropdown endpoints widen when any area is open,
    /// and everything else (including untagged endpoints) stays own-stores.</summary>
    public async Task<List<string>?> GetAccessibleStoreNamesAsync(string role, string? email)
    {
        if (UnrestrictedRoles.Contains(role)) return null;

        if (await IsAreaOpenForRoleAsync()) return null; // widened to full access

        return await ComputeOwnStoreNamesAsync(role, email);
    }

    /// <summary>Store names a restricted role owns via the email match — never
    /// widened by the access-area configuration. Used for write permissions
    /// (Action Plan notes/recommendations) so opening a page's view never grants
    /// write access to stores the user doesn't manage. Null = unrestricted.</summary>
    public async Task<List<string>?> GetOwnStoreNamesAsync(string role, string? email)
    {
        if (UnrestrictedRoles.Contains(role)) return null;
        return await ComputeOwnStoreNamesAsync(role, email);
    }

    private async Task<bool> IsAreaOpenForRoleAsync()
    {
        if (_policy == null) return false; // no config wired (e.g. tests) → restricted
        var area = _areaContext?.Area;

        if (string.IsNullOrEmpty(area)) return false;          // untagged endpoint → safe: own-stores
        if (area == AccessAreas.Shared) return await _policy.AnyOpenAsync(); // dropdowns widen if anything is open
        return !await _policy.IsRestrictedAsync(area);          // explicit area → open iff configured open
    }

    private async Task<List<string>> ComputeOwnStoreNamesAsync(string role, string? email)
    {
        // A role that isn't Admin/User but also isn't one of the known
        // store-restricted roles (e.g. an invalid/misspelled Bulk Upload
        // role) has no email column to match against, so it gets zero
        // accessible stores rather than falling through to full access.
        if (!RoleEmailColumns.TryGetValue(role, out var emailColumn)) return new List<string>();

        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return new List<string>();

        // Access always follows the latest uploaded period, regardless of
        // which month a report happens to be displaying — a rotation takes
        // effect for all historical data the moment the new period lands.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return new List<string>();

        var q = _db.StoreReferences.Where(s => s.Month == latest.Month && s.Year == latest.Year);
        q = q.Where(BuildEmailEqualsPredicate(emailColumn, normalized));

        return await q.Select(s => s.StoreName).Distinct().ToListAsync();
    }

    private static Expression<Func<StoreReference, bool>> BuildEmailEqualsPredicate(
        Expression<Func<StoreReference, string>> emailColumn, string normalizedEmail)
    {
        var lowerCall = Expression.Call(emailColumn.Body, nameof(string.ToLower), Type.EmptyTypes);
        var equals = Expression.Equal(lowerCall, Expression.Constant(normalizedEmail));
        return Expression.Lambda<Func<StoreReference, bool>>(equals, emailColumn.Parameters);
    }

    public async Task<bool> CanAccessStoreAsync(string role, string? email, string storeName)
    {
        if (!IsRestrictedRole(role)) return true;

        var accessible = await GetAccessibleStoreNamesAsync(role, email);
        return accessible != null && accessible.Any(s => string.Equals(s, storeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Write-permission check: whether the role/email may MANAGE (write to)
    /// the given store. Unlike <see cref="CanAccessStoreAsync"/> this ignores the
    /// access-area widening — a restricted role can only ever write to stores it
    /// actually owns, so opening a page's view never escalates write access.</summary>
    public async Task<bool> CanManageStoreAsync(string role, string? email, string storeName)
    {
        if (!IsRestrictedRole(role)) return true;

        var own = await GetOwnStoreNamesAsync(role, email);
        return own != null && own.Any(s => string.Equals(s, storeName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ResponsibleParty?> GetResponsiblePartyAsync(string storeName)
    {
        // Same "latest uploaded period" convention as GetAccessibleStoreNamesAsync,
        // so responsibility and access always agree on which period is current.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return null;

        var reference = await _db.StoreReferences
            .FirstOrDefaultAsync(s => s.StoreName == storeName && s.Month == latest.Month && s.Year == latest.Year)
            ?? await _db.StoreReferences
                .Where(s => s.StoreName == storeName)
                .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
                .FirstOrDefaultAsync();
        if (reference == null) return null;

        return ResolveResponsible(reference);
    }

    public ResponsibleParty ResolveResponsible(StoreReference reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.HeadManager) || !string.IsNullOrWhiteSpace(reference.HeadManagerEmail))
        {
            return new ResponsibleParty { Name = reference.HeadManager, Role = "Head_Manager", Email = reference.HeadManagerEmail };
        }

        return new ResponsibleParty { Name = reference.OperationConsultant, Role = "Operation_Consultant", Email = reference.OperationConsultantEmail };
    }
}
