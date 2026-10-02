using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MvcApp.Data;
using MvcApp.Models;
using Xunit;

namespace MvcApp.Tests.TestHelpers;

/// <summary>
/// The real application, started in memory (real routing, filters, session, anti-forgery and Razor
/// views) on an in-memory database instead of SQL Server. Used to test request-pipeline behaviour
/// that unit tests on single services cannot see.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<AppDbContext>
{
    public const string AdminPassword = "Admin-Password-123!";
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>Every log line the hosted app writes (for tests that assert on audit/security logging).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            // Drop the SQL Server registration of AppDbContext (and its options), then use an in-memory database.
            foreach (var descriptor in services.Where(d =>
                         d.ServiceType == typeof(AppDbContext) ||
                         (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext)))).ToList())
                services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<User> AddUserAsync(string email, string role, string password = AdminPassword, string? passwordHashOverride = "") =>
        AddUserCoreAsync(email, role, passwordHashOverride == "" ? BCrypt.Net.BCrypt.HashPassword(password, 4) : passwordHashOverride);

    private async Task<User> AddUserCoreAsync(string email, string role, string? hash)
    {
        var user = new User { Email = email, Phone = "+20100" + Math.Abs(email.GetHashCode()), Role = role, PasswordHash = hash };
        await SeedAsync(db => { db.Users.Add(user); return Task.CompletedTask; });
        return user;
    }

    private readonly SemaphoreSlim _adminLock = new(1, 1);
    private HttpClient? _adminClient;
    public const string AdminEmail = "tests-admin@example.com";

    /// <summary>One signed-in Admin client shared by a test class (the login endpoint is rate limited per IP).</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        await _adminLock.WaitAsync();
        try
        {
            if (_adminClient == null)
            {
                await AddUserAsync(AdminEmail, "Admin");
                _adminClient = await SignInAsync(AdminEmail, AdminPassword, admin: true);
            }
            return _adminClient;
        }
        finally { _adminLock.Release(); }
    }

    private readonly SemaphoreSlim _userLock = new(1, 1);
    private HttpClient? _userClient;
    public const string UserEmail = "tests-user@example.com";

    /// <summary>One signed-in "User"-role client (Home portal) shared by a test class.</summary>
    public async Task<HttpClient> UserClientAsync()
    {
        await _userLock.WaitAsync();
        try
        {
            if (_userClient == null)
            {
                await AddUserAsync(UserEmail, "User");
                _userClient = await SignInAsync(UserEmail, AdminPassword, admin: false);
            }
            return _userClient;
        }
        finally { _userLock.Release(); }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<HttpClient>>> _roleClients = new();

    /// <summary>A signed-in Home-portal client for the given account, created (and signed in) once per factory.</summary>
    public Task<HttpClient> RoleClientAsync(string email, string role) =>
        _roleClients.GetOrAdd(email, _ => new Lazy<Task<HttpClient>>(async () =>
        {
            await AddUserAsync(email, role);
            return await SignInAsync(email, AdminPassword, admin: false);
        })).Value;

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"), // the session and anti-forgery cookies are Secure
    });

    private static readonly Regex TokenInput = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Fetches a page and returns the anti-forgery token its form carries (the matching cookie is stored on the client).</summary>
    public static async Task<string> GetFormTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenInput.Match(html);
        Assert.True(match.Success, $"no anti-forgery token found on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Signs in through the real login pages (admin portal when <paramref name="admin"/>), returning a client that holds the session cookie.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password, bool admin)
    {
        var client = NewClient();
        var loginPath = admin ? "/adminlogin" : "/login";
        var token = await GetFormTokenAsync(client, loginPath);
        var post = await client.PostAsync(loginPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var complete = await client.GetAsync(post.Headers.Location!);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        return client;
    }
}
