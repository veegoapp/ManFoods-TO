using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>
/// Single source of truth for who may see, edit, delete, or assign roles to
/// which user accounts. The Super Admin (SuperAdminPolicy.SuperAdminEmail) is
/// not a separate stored Role — it's the one Admin-role account matching that
/// email — so this policy is what actually keeps a normal Admin from ever
/// seeing, managing, or promoting into an Admin/Super-Admin account. Every
/// user-management entry point (list, get, create, update, delete, bulk
/// upload) must route through here rather than re-implementing these checks,
/// so the rules stay consistent everywhere a user row can be read or changed.
/// </summary>
public static class UserManagementPolicy
{
    /// <summary>The full set of role values the app recognizes.</summary>
    public static readonly IReadOnlyList<string> ValidRoles = new[]
    {
        "Admin", "User", "Operation_Manager", "Operation_Consultant",
        "Head_Manager", "Senior_Operation_Consultant", "Operation_Director",
    };

    public static bool IsValidRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && ValidRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the canonical casing for a recognized role, or null if `role` isn't one of ValidRoles.</summary>
    public static string? NormalizeRole(string? role) =>
        ValidRoles.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this role value is the Admin-level role (granted to both normal Admins and the Super Admin).</summary>
    public static bool IsAdminRole(string? role) => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsSelf(string actorEmail, User target) =>
        string.Equals(actorEmail?.Trim(), target.Email, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the actor may even know this account exists — drives the
    /// Users list filter and the Get/Edit-by-id "not found" behavior. The
    /// Super Admin sees everyone; a normal Admin sees non-Admin accounts and
    /// their own row, but never another Admin or the Super Admin.
    /// </summary>
    public static bool CanView(string actorEmail, User target) =>
        SuperAdminPolicy.IsSuperAdmin(actorEmail) || !IsAdminRole(target.Role) || IsSelf(actorEmail, target);

    /// <summary>
    /// Whether the actor may submit an edit to this account's non-Role
    /// fields (email/phone/name/password). Same visibility rule as CanView —
    /// a normal Admin may edit their own basic info even though their own
    /// Role is "Admin", but no other Admin/Super-Admin row.
    /// </summary>
    public static bool CanEdit(string actorEmail, User target) => CanView(actorEmail, target);

    /// <summary>
    /// Whether the actor may set `target`'s password straight from the Edit User form
    /// (no current password asked for): anyone they may edit EXCEPT themselves — their
    /// own password changes only through Change Password, which verifies the current one.
    /// </summary>
    public static bool CanSetPasswordDirectly(string actorEmail, User target) =>
        CanEdit(actorEmail, target) && !IsSelf(actorEmail, target);

    /// <summary>
    /// Whether the actor may delete this account outright. Unlike CanEdit,
    /// there is no self-exception — a normal Admin can never delete their
    /// own or any other Admin account — and the Super Admin account can
    /// never be deleted by anyone, including itself.
    /// </summary>
    public static bool CanDelete(string actorEmail, User target)
    {
        if (SuperAdminPolicy.IsSuperAdmin(target.Email)) return false;
        if (SuperAdminPolicy.IsSuperAdmin(actorEmail)) return true;
        return !IsAdminRole(target.Role);
    }

    /// <summary>
    /// Whether the actor may create a new user with, or change an existing
    /// user's Role to, `role`. Only the Super Admin may assign the Admin
    /// role — this is what stops a normal Admin from creating or promoting
    /// into an Admin/Super-Admin account, via any entry point.
    /// </summary>
    public static bool CanAssignRole(string actorEmail, string? role) =>
        SuperAdminPolicy.IsSuperAdmin(actorEmail) || !IsAdminRole(role);

    /// <summary>
    /// Whether the actor may change `target`'s Role to `newRole`. Combines
    /// CanAssignRole with the no-self-promotion rule (nobody, Super Admin
    /// included, may change their own Role through this path) and the Super
    /// Admin's own Role being permanently pinned to "Admin".
    /// </summary>
    public static bool CanChangeRole(string actorEmail, User target, string newRole)
    {
        if (SuperAdminPolicy.IsSuperAdmin(target.Email))
            return string.Equals(newRole, target.Role, StringComparison.OrdinalIgnoreCase);

        if (IsSelf(actorEmail, target))
            return string.Equals(newRole, target.Role, StringComparison.OrdinalIgnoreCase);

        return CanEdit(actorEmail, target) && CanAssignRole(actorEmail, newRole);
    }

    /// <summary>
    /// Whether `target`'s Email may be changed to `newEmail`. The Super
    /// Admin's identity (its email) is permanently pinned — nobody, the
    /// Super Admin included, may change it away from
    /// SuperAdminPolicy.SuperAdminEmail through the edit form.
    /// </summary>
    public static bool CanChangeEmail(User target, string newEmail) =>
        !SuperAdminPolicy.IsSuperAdmin(target.Email) || string.Equals(newEmail?.Trim(), target.Email, StringComparison.OrdinalIgnoreCase);
}
