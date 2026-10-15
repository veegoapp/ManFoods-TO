using System.Net;
using MvcApp.Models;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Exit Interviews tab of Data Management: per-upload Update File, the System Result download, and no Period column.</summary>
public class ExitInterviewUploadHistoryTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ExitInterviewUploadHistoryTests(AppFactory app) => _app = app;

    private static readonly byte[] Xlsx = TestFiles.Workbook(new[] { "A" }, new[] { new[] { "1" } });

    private async Task<(int WithResult, int WithoutResult)> SeedAsync()
    {
        var withResult = new UploadLog { FileType = "exit_interviews", FileName = "new-upload.xlsx", UploadedBy = "t", FileContent = Xlsx, ResultContent = Xlsx };
        var withoutResult = new UploadLog { FileType = "exit_interviews", FileName = "old-upload.xlsx", UploadedBy = "t", FileContent = Xlsx };
        await _app.SeedAsync(db => { db.UploadLogs.RemoveRange(db.UploadLogs); db.UploadLogs.AddRange(withResult, withoutResult); return Task.CompletedTask; });
        return (withResult.Id, withoutResult.Id);
    }

    [Fact]
    public async Task ExitTab_ShowsUpdateFile_AndSystemResultOnlyWhereAResultWasSaved_AndNoPeriodColumn()
    {
        var (withResult, withoutResult) = await SeedAsync();
        var admin = await _app.AdminClientAsync();

        var res = await admin.GetAsync("/admin/dashboard/uploads?tab=exit");
        var html = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("new-upload.xlsx", html);
        Assert.Contains("old-upload.xlsx", html);
        Assert.Equal(2, CountOf(html, "dmOpenExitUpdateModal(this.dataset.logId")); // one Update File button per upload
        Assert.Contains("id=\"dmExitUpdateModal\"", html);
        Assert.Contains("action=\"/Admin/Dashboard/UpdateExitInterviews\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"DownloadUploadResult/{withResult}", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"DownloadUploadResult/{withoutResult}", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sort=period", html); // the Period column header is gone on this tab
    }

    [Fact]
    public async Task MonthlyTab_KeepsItsPeriodColumn_AndGetsNoExitUpdateButton()
    {
        await _app.SeedAsync(db =>
        {
            db.UploadLogs.RemoveRange(db.UploadLogs);
            db.UploadLogs.Add(new UploadLog { FileType = "active_employees", FileName = "active.xlsx", Month = 3, Year = 2026, UploadedBy = "t", FileContent = Xlsx });
            return Task.CompletedTask;
        });
        var admin = await _app.AdminClientAsync();

        var html = await (await admin.GetAsync("/admin/dashboard/uploads?tab=monthly")).Content.ReadAsStringAsync();

        Assert.Contains("active.xlsx", html);
        Assert.Contains("sort=period", html);
        Assert.Equal(0, CountOf(html, "dmOpenExitUpdateModal(this.dataset.logId")); // only the function definition would differ; no button rows here
        Assert.Contains("dmOpenUpdateModal(3, 2026", html); // the monthly Update File button is unchanged
    }

    [Fact]
    public async Task DownloadUploadResult_ReturnsTheSavedWorkbook_Or404_AndNeedsAnAdminSession()
    {
        var (withResult, withoutResult) = await SeedAsync();
        var admin = await _app.AdminClientAsync();

        var ok = await admin.GetAsync($"/Admin/Dashboard/DownloadUploadResult?id={withResult}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Xlsx, await ok.Content.ReadAsByteArrayAsync());
        Assert.Contains("System Result", ok.Content.Headers.ContentDisposition?.FileNameStar ?? ok.Content.Headers.ContentDisposition?.FileName ?? "");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/Admin/Dashboard/DownloadUploadResult?id={withoutResult}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Admin/Dashboard/DownloadUploadResult?id=999999")).StatusCode);

        var anonymous = _app.NewClient();
        var denied = await anonymous.GetAsync($"/Admin/Dashboard/DownloadUploadResult?id={withResult}");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        Assert.NotEqual(Xlsx, await denied.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UpdateExitInterviews_RejectsAPostWithoutAnAntiForgeryToken()
    {
        var (withResult, _) = await SeedAsync();
        var admin = await _app.AdminClientAsync();

        var res = await admin.PostAsync("/Admin/Dashboard/UpdateExitInterviews", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = withResult.ToString() }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private static int CountOf(string haystack, string needle)
    {
        var n = 0; var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
