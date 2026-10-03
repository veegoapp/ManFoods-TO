using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MvcApp.Diagnostics;

/// <summary>
/// Temporary performance diagnostics: finds out WHERE a slow page spends its time (opening a DB connection,
/// running a query, or the app itself). Everything is logged as a "[PERF]" warning (goes to logs/stdout on the
/// host) and only when something is actually slow, so normal requests add no log noise. Only the request path and
/// the SQL text are logged — never query strings, parameter values or any data.
/// </summary>
public sealed class PerfRequestStats
{
    public const string ItemKey = "perf.stats";
    public long DbTicks, ConnTicks;
    public int DbCount, ConnCount;
    public static PerfRequestStats? Of(HttpContext? ctx) => ctx?.Items[ItemKey] as PerfRequestStats;
}

public sealed class PerfDbInterceptor : DbCommandInterceptor
{
    private const int SlowCommandMs = 500;
    private const int SlowConnectionMs = 300;

    private readonly IHttpContextAccessor _http;
    private readonly ILogger<PerfDbInterceptor> _log;
    public PerfDbInterceptor(IHttpContextAccessor http, ILogger<PerfDbInterceptor> log) { _http = http; _log = log; }

    private void Command(DbCommand command, TimeSpan duration)
    {
        var stats = PerfRequestStats.Of(_http.HttpContext);
        if (stats != null) { Interlocked.Add(ref stats.DbTicks, duration.Ticks); Interlocked.Increment(ref stats.DbCount); }
        if (duration.TotalMilliseconds < SlowCommandMs) return;
        var sql = command.CommandText.Replace('\n', ' ').Replace('\r', ' ');
        _log.LogWarning("[PERF] slow SQL {Ms} ms on {Path}: {Sql}", (int)duration.TotalMilliseconds,
            _http.HttpContext?.Request.Path.Value, sql.Length > 400 ? sql[..400] + "..." : sql);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    { Command(command, eventData.Duration); return new ValueTask<DbDataReader>(result); }
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Command(command, eventData.Duration); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    { Command(command, eventData.Duration); return new ValueTask<int>(result); }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    { Command(command, eventData.Duration); return new ValueTask<object?>(result); }

    // Connection opening is where a dead pooled connection (idle too long) shows up as a long wait.
    public void ConnectionOpened(TimeSpan duration)
    {
        var stats = PerfRequestStats.Of(_http.HttpContext);
        if (stats != null) { Interlocked.Add(ref stats.ConnTicks, duration.Ticks); Interlocked.Increment(ref stats.ConnCount); }
        if (duration.TotalMilliseconds >= SlowConnectionMs)
            _log.LogWarning("[PERF] slow DB connection open {Ms} ms on {Path}", (int)duration.TotalMilliseconds, _http.HttpContext?.Request.Path.Value);
    }
}

public sealed class PerfConnectionInterceptor : DbConnectionInterceptor
{
    private readonly PerfDbInterceptor _perf;
    public PerfConnectionInterceptor(PerfDbInterceptor perf) => _perf = perf;

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    { _perf.ConnectionOpened(eventData.Duration); return Task.CompletedTask; }
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => _perf.ConnectionOpened(eventData.Duration);
}

public static class PerfDiagnosticsExtensions
{
    private const int SlowRequestMs = 1500;

    public static IServiceCollection AddPerfDiagnostics(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<PerfDbInterceptor>();
        services.AddSingleton<PerfConnectionInterceptor>();
        return services;
    }

    /// <summary>Logs a request that took long, with how much of it was DB connection time, SQL time and "everything else".</summary>
    public static IApplicationBuilder UsePerfDiagnostics(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var stats = new PerfRequestStats();
        context.Items[PerfRequestStats.ItemKey] = stats;
        var sw = Stopwatch.StartNew();
        try { await next(); }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds >= SlowRequestMs)
            {
                var conn = (int)TimeSpan.FromTicks(Interlocked.Read(ref stats.ConnTicks)).TotalMilliseconds;
                var db = (int)TimeSpan.FromTicks(Interlocked.Read(ref stats.DbTicks)).TotalMilliseconds;
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Perf").LogWarning(
                    "[PERF] slow request {Method} {Path} total={Total} ms, db-connect={Conn} ms ({ConnCount}x), sql={Sql} ms ({SqlCount} cmds), other={Other} ms, status={Status}",
                    context.Request.Method, context.Request.Path.Value, sw.ElapsedMilliseconds, conn, stats.ConnCount, db, stats.DbCount,
                    Math.Max(0, sw.ElapsedMilliseconds - conn - db), context.Response.StatusCode);
            }
        }
    });
}
