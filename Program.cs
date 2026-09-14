using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Extensions;
using MvcApp.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AddLocalization(opts => opts.ResourcesPath = "");
builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    // DataAnnotations ErrorMessage strings on the view models are resx keys,
    // resolved against the same SharedResource pair the views use.
    .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider =
        (type, factory) => factory.Create(typeof(MvcApp.Resources.SharedResource)));

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRateLimiter(options =>
{
    // Partitioned per client IP — a single shared/global bucket here would let
    // any one anonymous caller exhaust the entire app's login budget and lock
    // every user (including Admin) out of authenticating. Same pattern as the
    // "api" policy below; ForwardedHeadersOptions above already resolves the
    // real client IP behind the reverse proxy. Applies to /login, /adminlogin,
    // Forgot Password, and Admin Recover (all carry [EnableRateLimiting("login")]).
    options.AddPolicy("login", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0
        });
    });

    // Unlike "login" above (now per-IP too), the dashboard API surface gets
    // many parallel requests per page load from every logged-in user, so it
    // must be partitioned per client IP rather than sharing a single global
    // budget — otherwise a handful of concurrent users would exhaust it for
    // everyone. ForwardedHeadersOptions above already resolves the real
    // client IP behind the reverse proxy.
    options.AddPolicy("api", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 300,
            QueueLimit = 0
        });
    });

    options.RejectionStatusCode = 429;

    // Default rejection is a bare 429 with no body, which on the login
    // page looks exactly like the form silently doing nothing. Show a
    // visible message instead (fine to be this specific — single-admin
    // app, raw technical errors are OK to surface directly in the UI).
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "text/plain";
        await context.HttpContext.Response.WriteAsync(
            "Too many attempts — rate limit exceeded. Please wait about a minute and try again.", token);
    };
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"App_Data/keys"))
    .SetApplicationName("ManFoodsTO");

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = MvcApp.Extensions.SessionExtensions.SessionCookieName;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var connectionString = BuildConnectionString();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        // Action-plan detection (Services/StoreActionPlanService.cs) issues many
        // sequential per-store queries in a single background job after every
        // monthly upload. The default 30s command timeout was tuned for Neon's
        // low latency and started hitting "Execution Timeout Expired" against
        // MonsterASP's higher round-trip latency — raise it so a single slow
        // command doesn't fail the whole run.
        sql.CommandTimeout(120)));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddScoped<IDataFreshnessService, DataFreshnessService>();
builder.Services.AddSingleton<IBackgroundJobTracker, BackgroundJobTracker>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISessionValidationService, SessionValidationService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IStoreAccessService, StoreAccessService>();
builder.Services.AddScoped<IExitInterviewService, ExitInterviewService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<INinetyDayTurnoverService, NinetyDayTurnoverService>();
builder.Services.AddScoped<IRetentionService, RetentionService>();
builder.Services.AddScoped<IEarlyWarningService, EarlyWarningService>();
builder.Services.AddScoped<IScorecardService, ScorecardService>();
builder.Services.AddScoped<IStoreActionPlanService, StoreActionPlanService>();
builder.Services.AddScoped<IActionPlanRoleService, ActionPlanRoleService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IColorRulesService, ColorRulesService>();
builder.Services.AddScoped<IRecommendationTemplateService, RecommendationTemplateService>();
builder.Services.AddScoped<IActionPlanSeverityConfigService, ActionPlanSeverityConfigService>();

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
    KnownNetworks = { new IPNetwork(System.Net.IPAddress.Parse("127.0.0.1"), 8),
                      new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8),
                      new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12),
                      new IPNetwork(System.Net.IPAddress.Parse("100.64.0.0"), 10) }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Defense-in-depth on top of whatever HTTPS enforcement the reverse proxy
// already does — safe to run behind it since UseForwardedHeaders above
// already resolves the original client scheme via X-Forwarded-Proto.
app.UseHttpsRedirection();

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new MfLangCookieProvider()
    }
});

app.Use(async (context, next) =>
{
    var h = context.Response.Headers;

    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["X-Permitted-Cross-Domain-Policies"] = "none";

    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'self';";

    h.Remove("X-Powered-By");

    // The entire /api/* surface (see the "api" route below) serves
    // authenticated HR data — turnover, retention, exit interviews, early
    // warning, workforce/store data — as JSON. None of it should be kept by
    // the browser's cache or an intermediary proxy. Static assets under
    // wwwroot never match this prefix, so they're unaffected.
    if (context.Request.Path.StartsWithSegments("/api"))
        h["Cache-Control"] = "no-store";

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();


app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Account}/{action=Login}/{id?}");


app.MapGet("/", ctx =>
{
    ctx.Response.Redirect("/login");
    return Task.CompletedTask;
});

app.MapGet("/admin", ctx =>
{
    ctx.Response.Redirect("/adminlogin");
    return Task.CompletedTask;
});

app.MapGet("/home", ctx =>
{
    ctx.Response.Redirect("/login");
    return Task.CompletedTask;
});


app.MapControllerRoute(
    name: "language",
    pattern: "language/{action}/{id?}",
    defaults: new { controller = "Language" });


app.MapControllerRoute(
    name: "api",
    pattern: "api/{controller}/{action}/{id?}");


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed");
        if (!app.Environment.IsDevelopment())
            throw; // Fail fast in production — a broken DB must not serve traffic
    }
}

// One-off CLI mode: `dotnet run -- --run-signal-backfill` runs the existing
// historical signal backfill (StoreActionPlanService.RunHistoricalSignalBackfillAsync)
// against whatever database SQLSERVER_CONNECTION_STRING points to, using the
// exact same DI-wired services as the running app (no separate tool, no risk
// of the logic drifting from production), then exits without starting the
// web server. See .github/workflows/backfill-signals.yml for how this is
// triggered — manual dispatch only, same convention as db-migrate.yml.
if (args.Contains("--run-signal-backfill"))
{
    using var backfillScope = app.Services.CreateScope();
    var actionPlans = backfillScope.ServiceProvider.GetRequiredService<IStoreActionPlanService>();
    var written = await actionPlans.RunHistoricalSignalBackfillAsync();
    Console.WriteLine($"SIGNAL_BACKFILL_WRITTEN={written}");
    return;
}

// One-off, read-only CLI diagnostic: `dotnet run -- --diagnose-snapshots`
// reports how many store_action_plans / action_plan_recommendations /
// action_plan_metric_snapshots rows actually exist in the database, and
// whether Active plans' snapshot rows line up with their recommendation
// rows — used to investigate why the Monthly Performance table shows no
// data across every store. Read-only, no writes, exits without starting
// the web server.
if (args.Contains("--diagnose-snapshots"))
{
    using var diagScope = app.Services.CreateScope();
    var db = diagScope.ServiceProvider.GetRequiredService<AppDbContext>();

    var totalPlans = await db.StoreActionPlans.CountAsync();
    var activePlans = await db.StoreActionPlans.CountAsync(p => p.Status == "Active");
    var totalRecommendations = await db.ActionPlanRecommendations.CountAsync();
    var totalSnapshots = await db.ActionPlanMetricSnapshots.CountAsync();
    Console.WriteLine($"DIAG_TOTAL_PLANS={totalPlans}");
    Console.WriteLine($"DIAG_ACTIVE_PLANS={activePlans}");
    Console.WriteLine($"DIAG_TOTAL_RECOMMENDATIONS={totalRecommendations}");
    Console.WriteLine($"DIAG_TOTAL_SNAPSHOTS={totalSnapshots}");

    var sample = await db.StoreActionPlans
        .Where(p => p.Status == "Active")
        .OrderByDescending(p => p.CreatedAt)
        .Take(10)
        .ToListAsync();
    foreach (var plan in sample)
    {
        var recCount = await db.ActionPlanRecommendations.CountAsync(r => r.StoreActionPlanId == plan.Id);
        var snapCount = await db.ActionPlanMetricSnapshots.CountAsync(s => s.StoreActionPlanId == plan.Id);
        Console.WriteLine($"DIAG_PLAN id={plan.Id} store=\"{plan.StoreName}\" createdAt={plan.CreatedAt:O} createdMonth={plan.CreatedMonth} createdYear={plan.CreatedYear} lastEvalMonth={plan.LastEvaluatedMonth} lastEvalYear={plan.LastEvaluatedYear} recommendations={recCount} snapshots={snapCount}");
    }
    return;
}

// One-off, read-only CLI diagnostic: `dotnet run -- --diagnose-early-warning-scores`
// runs the exact same scoring pass as EarlyWarningService.GetWatchlistAsync
// (Admin role = no store restriction, no store/months/year/job filters, so it
// uses the latest Active Employees snapshot as its anchor period, same as the
// Early Warning page's default view) and reports the distribution of the
// underlying RiskScore (not just the 1-5 star bucket it gets mapped into) —
// used to check whether the >=7 RiskScore threshold for "High Risk" (4-5
// stars) is realistic given how rarely employees actually stack multiple
// significant risk factors at once. Read-only, no writes, exits without
// starting the web server.
if (args.Contains("--diagnose-early-warning-scores"))
{
    using var ewDiagScope = app.Services.CreateScope();
    var earlyWarning = ewDiagScope.ServiceProvider.GetRequiredService<IEarlyWarningService>();

    var watchlist = await earlyWarning.GetWatchlistAsync(
        store: null, role: "Admin", assignedName: null, months: null, year: null,
        om: null, oc: null, soc: null, od: null);

    Console.WriteLine($"EW_TOTAL_WATCHLIST={watchlist.Count}");

    var byScore = watchlist.GroupBy(r => r.RiskScore).OrderBy(g => g.Key);
    foreach (var g in byScore)
        Console.WriteLine($"EW_SCORE={g.Key} count={g.Count()}");

    var byStars = watchlist.GroupBy(r => r.Stars).OrderBy(g => g.Key);
    foreach (var g in byStars)
        Console.WriteLine($"EW_STARS={g.Key} count={g.Count()}");

    if (watchlist.Count > 0)
    {
        var scores = watchlist.Select(r => r.RiskScore).OrderBy(s => s).ToList();
        double Percentile(double p)
        {
            var idx = (int)Math.Ceiling(p / 100.0 * scores.Count) - 1;
            return scores[Math.Clamp(idx, 0, scores.Count - 1)];
        }
        Console.WriteLine($"EW_MIN_SCORE={scores.First()}");
        Console.WriteLine($"EW_MAX_SCORE={scores.Last()}");
        Console.WriteLine($"EW_MEAN_SCORE={scores.Average():F2}");
        Console.WriteLine($"EW_P50={Percentile(50)}");
        Console.WriteLine($"EW_P75={Percentile(75)}");
        Console.WriteLine($"EW_P90={Percentile(90)}");
        Console.WriteLine($"EW_P95={Percentile(95)}");
        Console.WriteLine($"EW_P99={Percentile(99)}");
    }

    var reasonCounts = watchlist
        .SelectMany(r => r.Reasons)
        .GroupBy(x => x.Type)
        .OrderByDescending(g => g.Count());
    foreach (var g in reasonCounts)
        Console.WriteLine($"EW_REASON_TYPE={g.Key} count={g.Count()}");

    return;
}

app.Run();


// Resolution order mirrors the previous Neon/Postgres setup's fallback chain, adapted
// for SQL Server: a full connection string first (what MonsterASP hands you directly
// from its control panel), then discrete parts assembled safely via
// SqlConnectionStringBuilder (handles escaping of special characters in the password),
// then a local-dev-only fallback so `dotnet run` still works with no secrets configured.
// No credentials are hardcoded anywhere below except that local/dev fallback, which uses
// SQL Server's Trusted_Connection (no username/password at all) rather than a literal
// credential.
static string BuildConnectionString()
{
    var fullConnectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
    if (!string.IsNullOrEmpty(fullConnectionString))
        return fullConnectionString;

    var mssqlHost = Environment.GetEnvironmentVariable("MSSQL_HOST");
    var mssqlPort = Environment.GetEnvironmentVariable("MSSQL_PORT") ?? "1433";
    var mssqlDatabase = Environment.GetEnvironmentVariable("MSSQL_DATABASE");
    var mssqlUser = Environment.GetEnvironmentVariable("MSSQL_USER");
    var mssqlPassword = Environment.GetEnvironmentVariable("MSSQL_PASSWORD");

    if (!string.IsNullOrEmpty(mssqlHost) && !string.IsNullOrEmpty(mssqlUser))
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = $"{mssqlHost},{mssqlPort}",
            InitialCatalog = mssqlDatabase,
            UserID = mssqlUser,
            Password = mssqlPassword,
            Encrypt = true,
            TrustServerCertificate = Environment.GetEnvironmentVariable("MSSQL_TRUST_SERVER_CERTIFICATE") == "true",
        };
        return builder.ConnectionString;
    }

    // A generic DATABASE_URL, if set, is treated as an already-complete
    // connection string (unlike the old Postgres setup, there's no widely-used
    // URI scheme for SQL Server connection strings to parse).
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
        return databaseUrl;

    // Local-dev-only fallback: SQL Server LocalDB with a trusted (Windows-integrated)
    // connection, so no credential is ever hardcoded here.
    return @"Server=(localdb)\MSSQLLocalDB;Database=manfoods;Trusted_Connection=True;TrustServerCertificate=True;";
}
