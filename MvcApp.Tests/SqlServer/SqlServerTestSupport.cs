using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Data;
using MvcApp.Services;
using Xunit;

namespace MvcApp.Tests.SqlServer;

/// <summary>
/// Configuration and SAFETY GUARDS for the real-SQL-Server tests.
///
/// These tests only ever read <c>TEST_SQLSERVER_CONNECTION_STRING</c>. They never call the application's
/// BuildConnectionString and never read SQLSERVER_CONNECTION_STRING / MSSQL_* / DATABASE_URL, so a developer
/// machine (or Replit shell) that has production secrets in its environment cannot leak them into a test run.
/// Before anything is created, dropped or connected to, the server must be localhost / 127.0.0.1 and the
/// database must be named mvcapp_test_*; anything else throws immediately.
/// </summary>
public static class SqlServerTestConfig
{
    public const string ConnectionStringVariable = "TEST_SQLSERVER_CONNECTION_STRING";
    public const string RequireVariable = "REQUIRE_SQLSERVER_TESTS";
    public const string DatabasePrefix = "mvcapp_test_";

    private static readonly Regex SafeDatabaseName = new("^mvcapp_test_[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public static string? RawConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static bool IsAvailable => RawConnectionString != null;

    /// <summary>Set (to "true") by the GitHub Actions sql-tests job: a missing connection string must FAIL there, never skip.</summary>
    public static bool Required =>
        string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "true", StringComparison.OrdinalIgnoreCase);

    public enum Decision { Run, Skip, FailMissing }

    /// <summary>The whole skip policy in one pure function: missing + required = FAIL (loud), missing + not required = skip.</summary>
    public static Decision Decide(bool available, bool required) =>
        available ? Decision.Run : required ? Decision.FailMissing : Decision.Skip;

    public static Decision Current => Decide(IsAvailable, Required);

    public static string SkipReason =>
        $"{ConnectionStringVariable} is not set, so the SQL Server tests are skipped. Point it at a LOCAL SQL Server " +
        $"(Server=localhost,...) to run them; databases named {DatabasePrefix}* are created and dropped automatically.";

    /// <summary>Throws when the variable is missing but REQUIRE_SQLSERVER_TESTS=true (the CI job), so the run fails loudly.</summary>
    public static void EnforceRequired()
    {
        if (Current == Decision.FailMissing)
            throw new InvalidOperationException(
                $"{RequireVariable}=true but {ConnectionStringVariable} is not set. The sql-tests job must provide a " +
                "SQL Server connection string; refusing to skip silently.");
    }

    /// <summary>Only a local server is acceptable: localhost or 127.0.0.1 (optionally "tcp:" prefixed / with a port).</summary>
    public static void AssertSafeServer(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var host = builder.DataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        var comma = host.IndexOf(',');
        if (comma >= 0) host = host[..comma];
        host = host.Trim();

        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && host != "127.0.0.1")
            throw new InvalidOperationException(
                $"Refusing to run SQL Server tests against server '{builder.DataSource}': only localhost or 127.0.0.1 is allowed.");
    }

    public static void AssertSafeDatabaseName(string databaseName)
    {
        if (string.IsNullOrEmpty(databaseName) || !SafeDatabaseName.IsMatch(databaseName))
            throw new InvalidOperationException(
                $"Refusing to touch database '{databaseName}': test databases must be named {DatabasePrefix}<letters/digits/underscore>.");
    }

    /// <summary>Server + database guard for a complete connection string (used before a web host is built on it).</summary>
    public static void AssertSafeConnection(string connectionString)
    {
        AssertSafeServer(connectionString);
        AssertSafeDatabaseName(new SqlConnectionStringBuilder(connectionString).InitialCatalog);
    }

    /// <summary>The base connection string with its database replaced by a guarded mvcapp_test_* name.</summary>
    public static string ForDatabase(string baseConnectionString, string databaseName)
    {
        AssertSafeServer(baseConnectionString);
        AssertSafeDatabaseName(databaseName);
        return new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = databaseName }.ConnectionString;
    }

    /// <summary>Connection to the server's master database, used only to CREATE / DROP guarded test databases.</summary>
    public static string AdminConnectionString()
    {
        var raw = RawConnectionString ?? throw new InvalidOperationException($"{ConnectionStringVariable} is not set.");
        AssertSafeServer(raw);
        return new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" }.ConnectionString;
    }
}

/// <summary>[Fact] that is skipped when TEST_SQLSERVER_CONNECTION_STRING is missing — unless REQUIRE_SQLSERVER_TESTS=true,
/// in which case it runs and the fixture fails with a clear message (no silent skip in CI).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (SqlServerTestConfig.Current == SqlServerTestConfig.Decision.Skip)
            Skip = SqlServerTestConfig.SkipReason;
    }
}

/// <summary>Creates and drops the throw-away mvcapp_test_* databases. Every entry point re-checks the guards.</summary>
public static class SqlServerTestDatabase
{
    public static string NewName() => SqlServerTestConfig.DatabasePrefix + Guid.NewGuid().ToString("N");

    public static async Task CreateAsync(string databaseName)
    {
        SqlServerTestConfig.AssertSafeDatabaseName(databaseName);
        await using var connection = new SqlConnection(SqlServerTestConfig.AdminConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"CREATE DATABASE [{databaseName}]");
    }

    /// <summary>Explicit DROP DATABASE (never EnsureDeleted): pools are cleared first and open sessions are kicked with
    /// SINGLE_USER ... ROLLBACK IMMEDIATE so the drop cannot be blocked by a lingering connection.</summary>
    public static async Task DropAsync(string databaseName)
    {
        SqlServerTestConfig.AssertSafeDatabaseName(databaseName);
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(SqlServerTestConfig.AdminConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection,
            $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{databaseName}]; END");
    }

    /// <summary>Drop with a few retries; throws if the database is still there afterwards (a leak must be visible).</summary>
    public static async Task DropWithRetryAsync(string databaseName)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { await DropAsync(databaseName); return; }
            catch (Exception ex) { last = ex; await Task.Delay(TimeSpan.FromSeconds(attempt)); }
        }
        throw new InvalidOperationException($"Could not drop test database '{databaseName}' after 3 attempts.", last);
    }

    /// <summary>Safety net for runs that were killed before cleanup: removes OLD mvcapp_test_* databases only.</summary>
    public static async Task DropStaleAsync(TimeSpan olderThan)
    {
        var stale = new List<string>();
        await using (var connection = new SqlConnection(SqlServerTestConfig.AdminConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // create_date is in the SQL Server host's local time, so compare against its own GETDATE().
            command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE 'mvcapp[_]test[_]%' AND create_date < DATEADD(MINUTE, -@minutes, GETDATE())";
            command.Parameters.AddWithValue("@minutes", (int)olderThan.TotalMinutes);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) stale.Add(reader.GetString(0));
        }

        foreach (var name in stale)
        {
            try { await DropAsync(name); }
            catch { /* best effort: another run may own it */ }
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>One fresh, guarded SQL Server database per test class (xUnit class fixture): created, schema built by EF's
/// EnsureCreated (the same call the application makes at startup), optionally seeded, and dropped afterwards.</summary>
public class SqlServerDatabaseFixture : IAsyncLifetime
{
    public string DatabaseName { get; private set; } = "";
    public string ConnectionString { get; private set; } = "";
    public bool Enabled { get; private set; }

    protected virtual Task SeedAsync(AppDbContext db) => Task.CompletedTask;

    public virtual async Task InitializeAsync()
    {
        SqlServerTestConfig.EnforceRequired();
        if (SqlServerTestConfig.Current != SqlServerTestConfig.Decision.Run) return; // skipped locally

        await SqlServerTestDatabase.DropStaleAsync(TimeSpan.FromHours(6));

        DatabaseName = SqlServerTestDatabase.NewName();
        ConnectionString = SqlServerTestConfig.ForDatabase(SqlServerTestConfig.RawConnectionString!, DatabaseName);
        await SqlServerTestDatabase.CreateAsync(DatabaseName);
        Enabled = true;
        try
        {
            await using var db = NewContext();
            await db.Database.EnsureCreatedAsync();
            await SeedAsync(db);
        }
        catch
        {
            await SqlServerTestDatabase.DropWithRetryAsync(DatabaseName);
            Enabled = false;
            throw;
        }
    }

    public virtual async Task DisposeAsync()
    {
        if (Enabled)
        {
            Enabled = false;
            await SqlServerTestDatabase.DropWithRetryAsync(DatabaseName);
        }
    }

    public AppDbContext NewContext()
    {
        SqlServerTestConfig.AssertSafeConnection(ConnectionString);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(120))
            .Options);
    }
}

/// <summary>
/// The real application host (routing, DI, UploadService, caches) started on a guarded SQL Server test database.
/// The production AppDbContext registration is removed and replaced — and the host refuses to start if no such
/// registration was found to replace — so the connection string Program.cs resolves from the environment is never used.
/// </summary>
public sealed class SqlServerAppFactory : WebApplicationFactory<AppDbContext>
{
    private readonly string _connectionString;

    public SqlServerAppFactory(string connectionString)
    {
        SqlServerTestConfig.AssertSafeConnection(connectionString);
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var existing = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext)))).ToList();
            if (existing.Count == 0)
                throw new InvalidOperationException("The application's AppDbContext registration was not found; refusing to start a test host on an unknown database.");
            foreach (var descriptor in existing) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(_connectionString, sql => sql.CommandTimeout(120)));
        });
    }

    /// <summary>Waits for the fire-and-forget jobs an upload starts (action-plan detection) so they cannot still be writing
    /// when the database is dropped. Throws a clear TimeoutException instead of waiting forever.</summary>
    public async Task WaitForBackgroundJobsAsync(TimeSpan timeout)
    {
        var tracker = Services.GetRequiredService<IBackgroundJobTracker>();
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var running = tracker.GetRecent(1000).Where(j => j.Status == "Running").Select(j => j.Label).ToList();
            if (running.Count == 0) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Background upload job(s) still running after {timeout.TotalSeconds:0}s: {string.Join("; ", running)}");
            await Task.Delay(200);
        }
    }
}

/// <summary>Class fixture: a guarded database plus the real app host on top of it. Disposal order is: wait for background
/// jobs (bounded), dispose the host, then DROP the database.</summary>
public sealed class SqlServerHostFixture : SqlServerDatabaseFixture
{
    public static readonly TimeSpan BackgroundJobTimeout = TimeSpan.FromSeconds(90);

    public SqlServerAppFactory? Factory { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (!Enabled) return;
        try
        {
            Factory = new SqlServerAppFactory(ConnectionString);
            _ = Factory.Services; // start the host (Program.cs runs EnsureCreated on the already-created test database)
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public override async Task DisposeAsync()
    {
        try
        {
            if (Factory != null) await Factory.WaitForBackgroundJobsAsync(BackgroundJobTimeout);
        }
        finally
        {
            if (Factory != null)
            {
                await Factory.DisposeAsync();
                Factory = null;
            }
            await base.DisposeAsync();
        }
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Factory!.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> action) =>
        InScopeAsync<object?>(async sp => { await action(sp); return null; });
}
