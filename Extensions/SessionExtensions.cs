namespace MvcApp.Extensions;

public static class SessionExtensions
{
    public static void SetUserSession(this ISession session, int userId, string email, string role, string? assignedName)
    {
        session.SetInt32("UserId", userId);
        session.SetString("Email", email);
        session.SetString("Role", role);
        session.SetString("AssignedName", assignedName ?? "");
    }

    public static int? GetUserId(this ISession session) => session.GetInt32("UserId");
    public static string GetEmail(this ISession session) => session.GetString("Email") ?? "";
    public static string GetRole(this ISession session) => session.GetString("Role") ?? "";
    public static string? GetAssignedName(this ISession session)
    {
        var v = session.GetString("AssignedName");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static bool IsAdmin(this ISession session)
    {
        var role = session.GetRole();
        return role == "Admin_Full" || role == "Admin_Read";
    }
}
