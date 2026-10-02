using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MvcApp.Data;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>L11 — every Excel upload is checked (size, type, signature, archive contents) before parsing; the bulk-user upload also caps rows and text lengths.</summary>
public class ExcelUploadHardeningTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ExcelUploadHardeningTests(AppFactory app) => _app = app;

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    // ── the shared guard ─────────────────────────────────────────────────────

    [Fact]
    public async Task NormalWorkbook_IsAccepted()
    {
        var file = TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, new[] { new[] { "a@example.com", "+201000000001", "A", "User" } }), "users.xlsx");
        Assert.Equal(ExcelUploadProblem.None, await ExcelUploadGuard.CheckAsync(file));
    }

    [Fact]
    public async Task LegacyXlsSignature_IsStillAccepted_AsBefore()
    {
        var ole2 = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 };
        Assert.Equal(ExcelUploadProblem.None, await ExcelUploadGuard.CheckAsync(TestFiles.Form(ole2, "old.xls")));
    }

    [Theory]
    [InlineData("users.csv")]
    [InlineData("users.exe")]
    [InlineData("users")]
    [InlineData("users.xlsx.txt")]
    public async Task WrongExtension_IsRejected(string name) =>
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, Array.Empty<string[]>()), name)));

    [Fact]
    public async Task FileThatOnlyLooksLikeExcel_IsRejected()
    {
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(Bytes("Email,Phone\na@b.c,1"), "users.xlsx"))); // text renamed
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(Bytes("MZ\u0090\u0000 not excel"), "users.xlsx"))); // an executable header
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(Array.Empty<byte>(), "users.xlsx")));
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, "users.xlsx"))); // broken zip
    }

    [Fact]
    public async Task ZipThatIsNotAWorkbook_IsRejected() =>
        Assert.Equal(ExcelUploadProblem.NotExcel, await ExcelUploadGuard.CheckAsync(TestFiles.Form(TestFiles.ZipWithPayload(0, workbookParts: false), "users.xlsx")));

    [Fact]
    public async Task FileOverTenMegabytes_IsRejected()
    {
        var big = new byte[ExcelUploadGuard.MaxFileBytes + 1];
        big[0] = 0x50; big[1] = 0x4B; big[2] = 3; big[3] = 4;
        Assert.Equal(ExcelUploadProblem.TooLarge, await ExcelUploadGuard.CheckAsync(TestFiles.Form(big, "users.xlsx")));
    }

    [Fact]
    public async Task ZipBomb_SmallOnDisk_HugeWhenUnpacked_IsRejected()
    {
        var bomb = TestFiles.ZipWithPayload(ExcelUploadGuard.MaxUncompressedBytes + 10 * 1024 * 1024);
        Assert.True(bomb.Length < 1024 * 1024, "the test file itself is small");

        Assert.Equal(ExcelUploadProblem.ExpandsTooMuch, await ExcelUploadGuard.CheckAsync(TestFiles.Form(bomb, "roster.xlsx")));
    }

    [Fact]
    public async Task ArchiveWithAbsurdlyManyParts_IsRejected() =>
        Assert.Equal(ExcelUploadProblem.ExpandsTooMuch,
            await ExcelUploadGuard.CheckAsync(TestFiles.Form(TestFiles.ZipWithPayload(0, extraEntries: ExcelUploadGuard.MaxArchiveEntries + 1), "roster.xlsx")));

    [Fact]
    public async Task ZipJustBelowTheLimits_IsAccepted()
    {
        var file = TestFiles.Form(TestFiles.ZipWithPayload(5 * 1024 * 1024, extraEntries: 20), "roster.xlsx");
        Assert.Equal(ExcelUploadProblem.None, await ExcelUploadGuard.CheckAsync(file));
    }

    // ── bulk-user upload ─────────────────────────────────────────────────────

    private static UserService NewUserService(AppDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sessions = new SessionValidationService(db, cache);
        var auth = new AuthService(db, NullLogger<AuthService>.Instance, cache, new HttpContextAccessor(), sessions);
        return new UserService(db, new StoreAccessService(db), sessions, auth);
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static string[] UserRow(int i, string? email = null, string? phone = null, string? name = null) =>
        new[] { email ?? $"user{i}@example.com", phone ?? "+2010000" + i.ToString("D5"), name ?? "User " + i, "User" };

    [Fact]
    public async Task BulkUsers_NormalFile_StillImportsEveryone()
    {
        var db = NewDb();
        var file = TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, Enumerable.Range(1, 25).Select(i => UserRow(i))), "users.xlsx");

        var (created, skipped, mismatches) = await NewUserService(db).UploadBulkUsersAsync(file, "admin@mcd.com");

        Assert.Equal(25, created);
        Assert.Empty(skipped);
        Assert.Empty(mismatches);
        Assert.Equal(25, await db.Users.CountAsync());
    }

    [Fact]
    public async Task BulkUsers_ExactlyTheRowLimit_IsAccepted()
    {
        var db = NewDb();
        var file = TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, Enumerable.Range(1, UserService.MaxBulkUploadRows).Select(i => UserRow(i))), "users.xlsx");

        var (created, _, _) = await NewUserService(db).UploadBulkUsersAsync(file, "admin@mcd.com");

        Assert.Equal(UserService.MaxBulkUploadRows, created);
    }

    [Fact]
    public async Task BulkUsers_OneRowOverTheLimit_IsRejected_AndNothingIsCreated()
    {
        var db = NewDb();
        var file = TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, Enumerable.Range(1, UserService.MaxBulkUploadRows + 1).Select(i => UserRow(i))), "users.xlsx");

        var ex = await Assert.ThrowsAsync<BulkUploadFileRejectedException>(() => NewUserService(db).UploadBulkUsersAsync(file, "admin@mcd.com"));

        Assert.Equal("Msg_BulkUploadTooManyRows", ex.ResourceKey);
        Assert.Equal(new object[] { UserService.MaxBulkUploadRows }, ex.Args);
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData("not an excel file", "users.xlsx", "Msg_OnlyExcelFiles")]
    [InlineData("a,b,c", "users.csv", "Msg_OnlyExcelFiles")]
    public async Task BulkUsers_InvalidFile_IsRejectedCleanly(string content, string name, string key)
    {
        var db = NewDb();

        var ex = await Assert.ThrowsAsync<BulkUploadFileRejectedException>(() => NewUserService(db).UploadBulkUsersAsync(TestFiles.Form(Bytes(content), name), "admin@mcd.com"));

        Assert.Equal(key, ex.ResourceKey);
        Assert.Empty(db.Users);
    }

    [Fact]
    public async Task BulkUsers_OversizedFile_IsRejected()
    {
        var big = new byte[ExcelUploadGuard.MaxFileBytes + 1];
        big[0] = 0x50; big[1] = 0x4B; big[2] = 3; big[3] = 4;
        var ex = await Assert.ThrowsAsync<BulkUploadFileRejectedException>(() => NewUserService(NewDb()).UploadBulkUsersAsync(TestFiles.Form(big, "users.xlsx"), "admin@mcd.com"));
        Assert.Equal("Msg_FileTooLarge", ex.ResourceKey);
    }

    [Fact]
    public async Task BulkUsers_ZipBomb_IsRejected_WithAMuchLowerLimitThanTheRosterUploads()
    {
        var db = NewDb();
        var bomb = TestFiles.ZipWithPayload(60L * 1024 * 1024); // far below the 200 MB roster limit, above the 50 MB bulk-user one

        var ex = await Assert.ThrowsAsync<BulkUploadFileRejectedException>(() => NewUserService(db).UploadBulkUsersAsync(TestFiles.Form(bomb, "users.xlsx"), "admin@mcd.com"));

        Assert.Equal("Msg_ExcelContentTooLarge", ex.ResourceKey);
        Assert.Equal(ExcelUploadProblem.None, await ExcelUploadGuard.CheckAsync(TestFiles.Form(bomb, "roster.xlsx"))); // …but fine as a roster-sized file
    }

    [Fact]
    public async Task BulkUsers_OverlongText_IsRejected_WithTheOffendingRowNumbers()
    {
        var db = NewDb();
        var rows = new List<string[]> { UserRow(1), UserRow(2, name: new string('N', InputLimits.PersonName + 1)), UserRow(3), UserRow(4, phone: new string('9', InputLimits.Phone + 1)) };
        var file = TestFiles.Form(TestFiles.Workbook(TestFiles.UserHeaders, rows), "users.xlsx");

        var ex = await Assert.ThrowsAsync<BulkUploadFileRejectedException>(() => NewUserService(db).UploadBulkUsersAsync(file, "admin@mcd.com"));

        Assert.Equal("Msg_BulkUploadTextTooLong", ex.ResourceKey);
        Assert.Equal("3, 5", Assert.Single(ex.Args));
        Assert.Empty(db.Users); // atomic: the valid rows were not created either
    }

    // ── through the real endpoints ───────────────────────────────────────────

    private static MultipartFormDataContent Upload(string token, byte[] bytes, string fileName, IDictionary<string, string>? extra = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (k, v) in extra ?? new Dictionary<string, string>()) form.Add(new StringContent(v), k);
        var content = new ByteArrayContent(bytes);
        form.Add(content, "File", fileName);
        return form;
    }

    [Fact]
    public async Task Endpoint_BulkUserUpload_ShowsACleanMessage_ForAnInvalidFile_AndImportsAValidOne()
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");

        var rejected = await admin.PostAsync("/admin/dashboard/uploadbulkusers", Upload(token, Bytes("<script>alert(1)</script>"), "users.xlsx"));
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        var page = await (await admin.GetAsync("/admin/dashboard/users")).Content.ReadAsStringAsync();
        Assert.Contains("Only Excel files (.xlsx / .xls) are allowed.", page);

        var ok = await admin.PostAsync("/admin/dashboard/uploadbulkusers",
            Upload(token, TestFiles.Workbook(TestFiles.UserHeaders, new[] { UserRow(7, email: "endpoint-import@example.com") }), "users.xlsx"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.True(await _app.QueryAsync(db => db.Users.AnyAsync(u => u.Email == "endpoint-import@example.com")));
    }

    [Fact]
    public async Task Endpoint_RosterUploads_RejectAFileThatIsNotExcel_BeforeAnyParsing()
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/uploads");

        var response = await admin.PostAsync("/admin/dashboard/uploadjobpayrollgroups", Upload(token, Bytes("MZ not excel"), "groups.xlsx"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Only+Excel+files", response.Headers.Location!.OriginalString.Replace("%20", "+"));
    }

    [Fact]
    public async Task Endpoint_RosterUploads_RejectAZipBomb_WithTheNewMessage()
    {
        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/uploads");
        var bomb = TestFiles.ZipWithPayload(ExcelUploadGuard.MaxUncompressedBytes + 10 * 1024 * 1024);

        var response = await admin.PostAsync("/admin/dashboard/uploadjobpayrollgroups", Upload(token, bomb, "groups.xlsx"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("far more data", Uri.UnescapeDataString(response.Headers.Location!.OriginalString).Replace('+', ' '));
    }
}
