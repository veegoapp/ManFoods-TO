using System.Net;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>Exports (reports and the Default Passwords file) must not hand out text cells that Excel can turn into
/// formulas — and must not change a single character of the data while doing it.</summary>
public class ExcelExportGuardTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ExcelExportGuardTests(AppFactory app) => _app = app;

    private static XLWorkbook RoundTrip(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new XLWorkbook(ms);
    }

    // ── the guard itself ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("=1+1")]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"click\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    [InlineData("=cmd|' /C calc'!A0")]
    [InlineData("-3.5 pts")]
    public void DangerousText_IsKeptByteForByte_ButMarkedAsPlainText(string text)
    {
        using var wb = new XLWorkbook();
        wb.AddWorksheet("S").Cell(1, 1).Value = text;

        ExcelExportGuard.Neutralize(wb);
        using var reopened = RoundTrip(wb);
        var cell = reopened.Worksheet("S").Cell(1, 1);

        Assert.Equal(text, cell.GetString());               // nothing added, removed or altered
        Assert.False(cell.HasFormula);
        Assert.True(cell.Style.IncludeQuotePrefix);          // Excel keeps it as text, even when edited
    }

    [Fact]
    public void NormalText_Numbers_Dates_AndRealFormulas_AreUntouched()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("S");
        ws.Cell(1, 1).Value = "Ahmed Ali";
        ws.Cell(2, 1).Value = "Store = 5";            // contains the symbol, but does not start with it
        ws.Cell(3, 1).Value = 42;
        ws.Cell(4, 1).Value = -7.5;
        ws.Cell(5, 1).Value = new DateTime(2026, 3, 1);
        ws.Cell(6, 1).Value = "";
        ws.Cell(7, 1).FormulaA1 = "=A3+A4";             // a formula the application writes on purpose
        ws.Cell(8, 1).FormulaA1 = "=IF(A3=0,\"\",A4/A3)";

        ExcelExportGuard.Neutralize(wb);
        using var reopened = RoundTrip(wb);
        var s = reopened.Worksheet("S");

        foreach (var r in new[] { 1, 2, 3, 4, 5 }) Assert.False(s.Cell(r, 1).Style.IncludeQuotePrefix, $"row {r}");
        Assert.Equal("Ahmed Ali", s.Cell(1, 1).GetString());
        Assert.Equal(42, s.Cell(3, 1).GetDouble());
        Assert.Equal(-7.5, s.Cell(4, 1).GetDouble());
        Assert.True(s.Cell(7, 1).HasFormula);
        Assert.Equal("A3+A4", s.Cell(7, 1).FormulaA1);
        Assert.True(s.Cell(8, 1).HasFormula);
        Assert.False(s.Cell(7, 1).Style.IncludeQuotePrefix);
    }

    [Fact]
    public void EveryGeneratedTemporaryPassword_StillWorksAsAPassword_AfterTheGuard()
    {
        // The generator's symbol set includes = + - @, so some passwords start with them. They must come out identical.
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("S");
        var passwords = Enumerable.Range(0, 600).Select(_ => PasswordPolicy.GenerateTemporaryPassword()).ToList();
        for (var i = 0; i < passwords.Count; i++) ws.Cell(i + 1, 1).Value = passwords[i];

        ExcelExportGuard.Neutralize(wb);
        using var reopened = RoundTrip(wb);

        for (var i = 0; i < passwords.Count; i++)
        {
            var cell = reopened.Worksheet("S").Cell(i + 1, 1);
            Assert.Equal(passwords[i], cell.GetString());
            Assert.Equal("=+-@".Contains(passwords[i][0]), cell.Style.IncludeQuotePrefix);
        }
        Assert.Contains(passwords, p => "=+-@".Contains(p[0]));   // the case really occurs in the sample
    }

    // ── through the real endpoints ───────────────────────────────────────────

    [Fact]
    public async Task DefaultPasswordsFile_MarksDangerousTextCells_AndKeepsTheirValues()
    {
        var user = await _app.AddUserAsync("guard-pending@example.com", "User", passwordHashOverride: null);
        await _app.SeedAsync(async db => (await db.Users.SingleAsync(u => u.Id == user.Id)).Phone = "=cmd|' /C calc'!A0");

        var admin = await _app.AdminClientAsync();
        var token = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");
        var response = await admin.PostAsync("/admin/dashboard/generatedefaultpasswords",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var wb = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var ws = wb.Worksheet("Default Passwords");
        var row = ws.RowsUsed().Skip(1).Single(r => r.Cell(1).GetString() == "guard-pending@example.com");
        Assert.Equal("=cmd|' /C calc'!A0", row.Cell(2).GetString());
        Assert.True(row.Cell(2).Style.IncludeQuotePrefix);
        Assert.False(row.Cell(1).Style.IncludeQuotePrefix);          // an ordinary value is left alone
        Assert.True(row.Cell(3).GetString().Length >= 12);           // the temporary password is still there, unchanged in length/content
    }

    [Fact]
    public async Task ReportExport_MarksDangerousTextFromUploadedData()
    {
        await _app.SeedAsync(db =>
        {
            db.StoreReferences.Add(new StoreReference { Month = 3, Year = 2026, StoreName = "Guard Store" });
            db.ActiveEmployees.Add(new ActiveEmployee { Month = 3, Year = 2026, EmployeeId = "9001", Name = "=HYPERLINK(\"http://evil.example\",\"x\")", Store = "Guard Store", JobTitle = "+Crew", HireDate = new DateOnly(2025, 1, 1) });
            return Task.CompletedTask;
        });

        var user = await _app.UserClientAsync();
        var response = await user.GetAsync("/home/dashboard/export?reportType=workforce&month=3&year=2026");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var wb = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var cells = wb.Worksheets.SelectMany(w => w.CellsUsed()).ToList();
        var name = Assert.Single(cells, c => c.GetString() == "=HYPERLINK(\"http://evil.example\",\"x\")");
        Assert.True(name.Style.IncludeQuotePrefix);
        Assert.False(name.HasFormula);
        var job = cells.First(c => c.GetString() == "+Crew");
        Assert.True(job.Style.IncludeQuotePrefix);
        // the report's own formulas, if any, are still formulas
        Assert.DoesNotContain(cells.Where(c => c.HasFormula), c => c.Style.IncludeQuotePrefix);
    }
}

/// <summary>Responses that carry a temporary password or an OTP must never be stored by a browser or a proxy cache.</summary>
public class CredentialResponsesAreNotCachedTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public CredentialResponsesAreNotCachedTests(AppFactory app) => _app = app;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient admin, string path, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = await AppFactory.GetFormTokenAsync(admin, "/admin/dashboard/users");
        return await admin.PostAsync(path, new FormUrlEncodedContent(form));
    }

    private static void AssertNoStore(HttpResponseMessage response, string what)
    {
        var cache = string.Join(",", response.Headers.GetValues("Cache-Control"));
        Assert.True(cache.Contains("no-store", StringComparison.OrdinalIgnoreCase), $"{what}: Cache-Control was '{cache}'");
        Assert.Contains("no-cache", response.Headers.GetValues("Pragma"));
    }

    [Fact]
    public async Task SingleTemporaryPassword_AndTheBulkPasswordsFile_AreNoStore_AndStillWorkAsBefore()
    {
        var admin = await _app.AdminClientAsync();
        var pending = await _app.AddUserAsync("nostore-pending@example.com", "User", passwordHashOverride: null);

        var single = await PostAsync(admin, "/admin/dashboard/generatedefaultpassword", new() { ["id"] = pending.Id.ToString() });
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        AssertNoStore(single, "generatedefaultpassword");
        Assert.Equal("application/json", single.Content.Headers.ContentType?.MediaType);
        Assert.True(System.Text.Json.JsonDocument.Parse(await single.Content.ReadAsStringAsync()).RootElement.TryGetProperty("password", out _));

        var bulk = await PostAsync(admin, "/admin/dashboard/generatedefaultpasswords", new());
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
        AssertNoStore(bulk, "generatedefaultpasswords");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bulk.Content.Headers.ContentType?.MediaType);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await bulk.Content.ReadAsByteArrayAsync())[..2]));
    }

    // generateotp / generateadminotp delete the previous OTP with ExecuteDelete, which the in-memory test database does not
    // support, so they are checked on the endpoint's declared cache policy (the same attribute the other two carry).
    [Theory]
    [InlineData("GenerateDefaultPassword")]
    [InlineData("GenerateDefaultPasswords")]
    [InlineData("GenerateOtp")]
    [InlineData("GenerateAdminOtp")]
    public void EveryCredentialReturningAdminAction_DeclaresNoStore(string action)
    {
        var endpoints = _app.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s => s.Endpoints)
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is { } d
                        && d.ControllerName == "Dashboard" && d.ActionName == action && d.RouteValues["area"] == "Admin")
            .ToList();
        var endpoint = Assert.Single(endpoints);
        var cache = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.ResponseCacheAttribute>();
        Assert.NotNull(cache);
        Assert.True(cache!.NoStore);
        Assert.Equal(Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None, cache.Location);
    }
}
