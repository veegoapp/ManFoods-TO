using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Services;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 10;
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("api", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 60;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "manfoods.session";
});

var connectionString = BuildConnectionString();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IExitInterviewService, ExitInterviewService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<INinetyDayTurnoverService, NinetyDayTurnoverService>();
builder.Services.AddScoped<IRetentionService, RetentionService>();
builder.Services.AddScoped<IEarlyWarningService, EarlyWarningService>();
builder.Services.AddScoped<IScorecardService, ScorecardService>();
builder.Services.AddScoped<ITargetsService, TargetsService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IGeminiService, GeminiService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthorization();

// ── Areas ─────────────────────────────────────────────
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Account}/{action=Login}/{id?}");

// ── Root redirects ─────────────────────────────────────
app.MapGet("/", ctx => { ctx.Response.Redirect("/home/account/login"); return Task.CompletedTask; });
app.MapGet("/admin", ctx => { ctx.Response.Redirect("/admin/account/login"); return Task.CompletedTask; });
app.MapGet("/home", ctx => { ctx.Response.Redirect("/home/account/login"); return Task.CompletedTask; });

// ── API (no area prefix) ───────────────────────────────
app.MapControllerRoute(
    name: "api",
    pattern: "api/{controller}/{action}/{id?}");

// ── Default (fallback) ─────────────────────────────────
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("DB setup skipped: {Message}", ex.Message);
    }
}

app.Run();

static string BuildConnectionString()
{
    var neonUrl = Environment.GetEnvironmentVariable("NEON_DATABASE_URL");
    if (!string.IsNullOrEmpty(neonUrl) && (neonUrl.StartsWith("postgresql://") || neonUrl.StartsWith("postgres://")))
        return ParsePostgresUrl(neonUrl);

    var pgHost     = Environment.GetEnvironmentVariable("PGHOST");
    var pgPort     = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
    var pgUser     = Environment.GetEnvironmentVariable("PGUSER");
    var pgPassword = Environment.GetEnvironmentVariable("PGPASSWORD");
    var pgDatabase = Environment.GetEnvironmentVariable("PGDATABASE");

    if (!string.IsNullOrEmpty(pgHost) && !string.IsNullOrEmpty(pgUser))
        return $"Host={pgHost};Port={pgPort};Database={pgDatabase};Username={pgUser};Password={pgPassword}";

    var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(dbUrl) && (dbUrl.StartsWith("postgresql://") || dbUrl.StartsWith("postgres://")))
        return ParsePostgresUrl(dbUrl);

    return "Host=localhost;Port=5432;Database=manfoods;Username=postgres;Password=postgres";
}

static string ParsePostgresUrl(string url)
{
    try
    {
        var uri      = new Uri(url);
        var host     = uri.Host;
        var dbPort   = uri.Port > 0 ? uri.Port : 5432;
        var db       = uri.AbsolutePath.TrimStart('/').Split('?')[0];
        var userInfo = uri.UserInfo.Split(':');
        var user     = Uri.UnescapeDataString(userInfo[0]);
        var pass     = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";

        var cs = $"Host={host};Port={dbPort};Database={db};Username={user};Password={pass}";
        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(query))
            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0] == "sslmode" && !string.IsNullOrEmpty(kv[1]))
                    cs += $";SslMode={char.ToUpper(kv[1][0]) + kv[1][1..]}";
            }
        return cs;
    }
    catch { return url; }
}
