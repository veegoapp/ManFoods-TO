---
name: Manfoods MVC Conversion
description: Key decisions and quirks from converting Manfoods HR analytics from Node.js/TS to ASP.NET Core 9 MVC.
---

## DB connection
- Use individual PGHOST/PGPORT/PGUSER/PGPASSWORD/PGDATABASE env vars (set by Replit createDatabase()) instead of DATABASE_URL URL format — Npgsql rejects the raw postgres:// URL with `?sslmode` query param.
- Use `EnsureCreated()` not `Migrate()` (no migration files exist; EF creates schema from model).

## Packages
- BCrypt.Net-Next 4.0.3 and ClosedXML 0.102.3 restore successfully from NuGet on Replit .NET 9.
- dotnet-ef global tool v10.x conflicts with .NET 9 SDK — do not install it.

## Razor views
- `@new SomeType(args).Method()` fails in Razor. Always use `@(new SomeType(args).Method())` with explicit parentheses.
- Tuple arrays (`new[] { ("a","b"), ... }`) fail in Razor `@{ }` blocks — repeat the HTML instead.

## Session auth
- Session keys: UserId (int), Email, Role, AssignedName (string, empty if null).
- `Extensions/SessionExtensions.cs` provides helpers; `Filters/RequireAuthAttribute` and `RequireRoleAttribute` enforce auth.

## Default admin
- admin@manfoods.com / admin123 (seeded at startup if users table is empty).

**Why:** Avoids re-discovering these blockers on future sessions.
**How to apply:** Check these before adding features to this project.
