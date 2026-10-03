using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using Xunit;

namespace MvcApp.Tests.SqlServer;

/// <summary>
/// REPORT-ONLY comparison of the two ways this application's schema comes into existence:
///   A  — EF's EnsureCreated (what Program.cs runs at startup; the fixture database)
///   B  — scripts/migrate.sql executed on an EMPTY database
///   C  — EnsureCreated first, then scripts/migrate.sql on top (the real production sequence)
/// It also runs migrate.sql twice on B (the script claims to be safe to re-run on every deploy).
///
/// Differences are NOT asserted: this test never fails because of them. Everything is written to a report file
/// (TEST_SCHEMA_REPORT_PATH, uploaded by the CI job as an artifact) with each finding marked UNEXPECTED or ALLOWED
/// (with a reason). Once the findings have been triaged, reviewed ones move into <see cref="Allowed"/> and the test
/// can be tightened to fail on any remaining UNEXPECTED line. Only infrastructure problems (cannot create a database,
/// cannot connect) fail the test. Indexes, constraints and defaults are not compared yet — columns only.
/// </summary>
[Trait("Category", "SqlServer")]
public class SqlServerSchemaReportTests : IClassFixture<SqlServerDatabaseFixture>
{
    /// <summary>Reviewed, accepted differences: "table" or "table.column" → why it is fine.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["__EFMigrationsHistory"] = "EF history table; this app uses EnsureCreated and never EF migrations.",
    };

    private readonly SqlServerDatabaseFixture _fx;
    public SqlServerSchemaReportTests(SqlServerDatabaseFixture fx) => _fx = fx;

    [SqlServerFact]
    public async Task EnsureCreatedSchema_VersusMigrateSql_IsReportedNotEnforced()
    {
        var report = new StringBuilder();
        void Line(string text = "") => report.AppendLine(text);
        var unexpected = 0;
        void Finding(string text) { unexpected++; Line("UNEXPECTED  " + text); }

        string? dbB = null, dbC = null;
        try
        {
            Line("SQL Server schema report (report-only; differences do not fail the build)");
            Line("A = EnsureCreated | B = migrate.sql on an empty database | C = EnsureCreated, then migrate.sql");
            Line("Compared: tables and columns (type, length, nullability). Not compared yet: indexes, constraints, defaults.");
            Line();

            var scriptPath = FindMigrateSql();
            if (scriptPath == null)
            {
                Finding("scripts/migrate.sql was not found above " + AppContext.BaseDirectory + "; nothing to compare.");
            }
            else
            {
                var script = await File.ReadAllTextAsync(scriptPath);
                Line("script: " + scriptPath);
                var baseConnection = SqlServerTestConfig.RawConnectionString!;

                // A — already created by the fixture.
                var columnsA = await ReadColumnsAsync(_fx.ConnectionString);
                Line($"A: {columnsA.Count} columns in {columnsA.Keys.Select(k => k.Split('.')[0]).Distinct().Count()} tables");

                // B — migrate.sql alone, twice.
                dbB = SqlServerTestDatabase.NewName();
                await SqlServerTestDatabase.CreateAsync(dbB);
                var connectionB = SqlServerTestConfig.ForDatabase(baseConnection, dbB);
                var firstRun = await TryRunScriptAsync(connectionB, script);
                if (firstRun != null) Finding("B: migrate.sql FAILED on an empty database: " + firstRun);
                else Line("B: migrate.sql ran on an empty database: OK");
                var secondRun = firstRun == null ? await TryRunScriptAsync(connectionB, script) : "skipped (first run failed)";
                if (secondRun != null && firstRun == null) Finding("B: running migrate.sql a SECOND time failed (it should be re-runnable): " + secondRun);
                else if (firstRun == null) Line("B: second run of migrate.sql: OK (re-runnable)");

                // C — EnsureCreated, then migrate.sql on top.
                dbC = SqlServerTestDatabase.NewName();
                await SqlServerTestDatabase.CreateAsync(dbC);
                var connectionC = SqlServerTestConfig.ForDatabase(baseConnection, dbC);
                await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionC).Options))
                    await db.Database.EnsureCreatedAsync();
                var runC = await TryRunScriptAsync(connectionC, script);
                if (runC != null) Finding("C: migrate.sql FAILED on top of an EnsureCreated database (the production sequence): " + runC);
                else Line("C: migrate.sql ran on top of an EnsureCreated database: OK");

                Line();
                if (firstRun == null)
                {
                    Line("── A (EnsureCreated)  vs  B (migrate.sql only) ──");
                    unexpected += Compare(report, "EnsureCreated", columnsA, "migrate.sql", await ReadColumnsAsync(connectionB));
                }
                if (runC == null)
                {
                    Line();
                    Line("── A (EnsureCreated)  vs  C (EnsureCreated + migrate.sql): what the script changes on an existing database ──");
                    unexpected += Compare(report, "EnsureCreated", columnsA, "after migrate.sql", await ReadColumnsAsync(connectionC));
                }
            }
        }
        finally
        {
            Line();
            Line($"UNEXPECTED_COUNT={unexpected}");
            WriteReport(report.ToString());

            // Cleanup is attempted even if the comparison threw; a leak throws (visible) instead of being swallowed.
            if (dbB != null) await SqlServerTestDatabase.DropWithRetryAsync(dbB);
            if (dbC != null) await SqlServerTestDatabase.DropWithRetryAsync(dbC);
        }
        // Report-only: no assertion on `unexpected`.
    }

    // ── comparison ───────────────────────────────────────────────────────────

    private static int Compare(StringBuilder report, string leftName, Dictionary<string, string> left, string rightName, Dictionary<string, string> right)
    {
        var unexpected = 0;
        void Emit(string key, string text)
        {
            var table = key.Split('.')[0];
            if (Allowed.TryGetValue(key, out var why) || Allowed.TryGetValue(table, out why)) report.AppendLine("ALLOWED     " + text + "  — " + why);
            else { unexpected++; report.AppendLine("UNEXPECTED  " + text); }
        }

        foreach (var key in left.Keys.Where(k => !right.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            Emit(key, $"only in {leftName}: {key} ({left[key]})");
        foreach (var key in right.Keys.Where(k => !left.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            Emit(key, $"only in {rightName}: {key} ({right[key]})");
        foreach (var key in left.Keys.Where(k => right.TryGetValue(k, out var other) && other != left[k]).OrderBy(k => k, StringComparer.Ordinal))
            Emit(key, $"differs: {key}: {leftName}={left[key]} vs {rightName}={right[key]}");

        report.AppendLine($"({unexpected} unexpected difference(s))");
        return unexpected;
    }

    /// <summary>"table.column" → "type(length) NULL|NOT NULL" for every base-table column.</summary>
    private static async Task<Dictionary<string, string>> ReadColumnsAsync(string connectionString)
    {
        SqlServerTestConfig.AssertSafeConnection(connectionString);
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, ISNULL(c.CHARACTER_MAXIMUM_LENGTH, 0), c.IS_NULLABLE " +
            "FROM INFORMATION_SCHEMA.COLUMNS c " +
            "JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var length = reader.GetInt32(3);
            var type = reader.GetString(2) + (length == 0 ? "" : length == -1 ? "(max)" : $"({length})");
            columns[$"{reader.GetString(0)}.{reader.GetString(1)}"] = type + (reader.GetString(4) == "YES" ? " NULL" : " NOT NULL");
        }
        return columns;
    }

    // ── running scripts/migrate.sql ──────────────────────────────────────────

    /// <summary>Same batching as Tools/DbMigrator (split on lines that are exactly GO), executed with the guarded test connection
    /// only — DbMigrator itself is never started, so it can never pick up a production connection string.</summary>
    private static async Task<string?> TryRunScriptAsync(string connectionString, string script)
    {
        SqlServerTestConfig.AssertSafeConnection(connectionString);
        var batches = script.Split('\n')
            .Aggregate(new List<StringBuilder> { new() }, (acc, line) =>
            {
                if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)) acc.Add(new StringBuilder());
                else acc[^1].AppendLine(line);
                return acc;
            })
            .Select(b => b.ToString())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            foreach (var batch in batches)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 300;
                await command.ExecuteNonQueryAsync();
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? FindMigrateSql()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "migrate.sql");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void WriteReport(string text)
    {
        var path = Environment.GetEnvironmentVariable("TEST_SCHEMA_REPORT_PATH");
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(AppContext.BaseDirectory, "schema-report.txt");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, text);
        }
        catch { /* the report is a diagnostic; failing to write it must not fail the run */ }
        Console.WriteLine(text);
    }
}
