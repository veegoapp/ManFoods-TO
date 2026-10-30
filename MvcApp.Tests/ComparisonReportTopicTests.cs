using System.Net;
using ClosedXML.Excel;
using MvcApp.Models;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>The Comparison Excel report is one topic at a time (the Comparisons page's topics); topics whose data has no
/// store / job-title filter refuse those filters instead of silently comparing unfiltered numbers.</summary>
public class ComparisonReportTopicTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public ComparisonReportTopicTests(AppFactory app) => _app = app;

    [Theory]
    [InlineData(null, ComparisonTopics.Turnover)]
    [InlineData("", ComparisonTopics.Turnover)]
    [InlineData("nonsense", ComparisonTopics.Turnover)]
    [InlineData("scorecard", ComparisonTopics.Scorecard)]
    [InlineData("drivers", ComparisonTopics.Drivers)]
    public void Normalize_KnownTopicsPass_AnythingElseIsTurnover(string? topic, string expected) =>
        Assert.Equal(expected, ComparisonTopics.Normalize(topic));

    [Fact]
    public void EveryTopicIsKnown_AndOnlyTheDataWithoutThoseFiltersIgnoresThem()
    {
        Assert.Equal(8, ComparisonTopics.All.Length);
        Assert.Equal(new[] { ComparisonTopics.Scorecard }, ComparisonTopics.All.Where(ComparisonTopics.IgnoresStore));
        Assert.Equal(new[] { ComparisonTopics.Ninety, ComparisonTopics.Retention, ComparisonTopics.Scorecard }, ComparisonTopics.All.Where(ComparisonTopics.IgnoresJob));
    }

    [Fact]
    public void UnsupportedFilterMessage_NamesTheFilters_AndIsNullWhenAllIsFine()
    {
        Assert.Null(ComparisonTopics.UnsupportedFilterMessage(ComparisonTopics.Turnover, storeSet: true, jobSet: true));
        Assert.Null(ComparisonTopics.UnsupportedFilterMessage(ComparisonTopics.Scorecard, storeSet: false, jobSet: false));
        Assert.Contains("Store and Job Title", ComparisonTopics.UnsupportedFilterMessage(ComparisonTopics.Scorecard, storeSet: true, jobSet: true));
        Assert.Contains("Job Title", ComparisonTopics.UnsupportedFilterMessage(ComparisonTopics.Retention, storeSet: false, jobSet: true));
    }

    [Fact]
    public void TopicsFromAnotherPage_FollowThatPagesAccessRules()
    {
        Assert.Equal("scorecard", ComparisonTopics.SourceReportId(ComparisonTopics.Scorecard));
        Assert.Equal("retention", ComparisonTopics.SourceReportId(ComparisonTopics.Retention));
        Assert.Equal("ninety-day", ComparisonTopics.SourceReportId(ComparisonTopics.Ninety));
        Assert.Equal("exit-interviews", ComparisonTopics.SourceReportId(ComparisonTopics.Exit));
        Assert.Equal("exit-interviews", ComparisonTopics.SourceReportId(ComparisonTopics.Drivers));
        Assert.Null(ComparisonTopics.SourceReportId(ComparisonTopics.Turnover));
        // every source id is a real catalog report, so the access lookup can never silently find nothing
        foreach (var topic in ComparisonTopics.All)
            if (ComparisonTopics.SourceReportId(topic) is { } id) Assert.NotNull(MvcApp.Models.ViewModels.ReportCatalog.Find(id));
    }

    [Theory]
    [InlineData("topic=scorecard&jobs=Crew")]
    [InlineData("topic=scorecard&store=Any")]
    [InlineData("topic=ninety&jobs=Crew")]
    [InlineData("topic=retention&jobsB=Crew")]
    public async Task Export_RefusesAFilterTheTopicCannotHonour(string query)
    {
        var admin = await _app.AdminClientAsync();
        var response = await admin.GetAsync($"/admin/dashboard/export?reportType=comparisons&{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── the whole report on data: every topic builds the sheets the Comparisons page's tabs mirror ──────

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    /// <summary>Two stores, January and February of 2025 and 2026: a roster, resignations (one early leaver) and store references.</summary>
    private async Task EnsureSeededAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            await _app.SeedAsync(db =>
            {
                foreach (var year in new[] { 2025, 2026 })
                foreach (var month in new[] { 1, 2 })
                {
                    var first = new DateOnly(year, month, 1);
                    db.ActiveEmployees.AddRange(
                        new ActiveEmployee { EmployeeId = "E1", Name = "One", Store = "Store A", JobTitle = "Crew", Gender = "Male", PayrollGroup = "G1", HireDate = first.AddYears(-2), Month = month, Year = year },
                        new ActiveEmployee { EmployeeId = "E2", Name = "Two", Store = "Store A", JobTitle = "Manager", Gender = "Female", PayrollGroup = "G2", HireDate = first.AddYears(-3), Month = month, Year = year },
                        new ActiveEmployee { EmployeeId = "E3", Name = "Three", Store = "Store B", JobTitle = "Crew", Gender = "Female", PayrollGroup = "G1", HireDate = first.AddDays(3), Month = month, Year = year },
                        new ActiveEmployee { EmployeeId = "E4", Name = "Four", Store = "Store B", JobTitle = "Crew", Gender = "Male", PayrollGroup = "G1", HireDate = first.AddMonths(-8), Month = month, Year = year });
                    db.Resignations.AddRange(
                        new Resignation { EmployeeId = "R1", Name = "Gone One", Store = "Store A", JobTitle = "Crew", Gender = "Male", PayrollGroup = "G1", HireDate = first.AddYears(-1), ResignationDate = first.AddDays(10), Month = month, Year = year },
                        new Resignation { EmployeeId = "R2", Name = "Gone Two", Store = "Store B", JobTitle = "Crew", Gender = "Female", PayrollGroup = "G1", HireDate = first.AddDays(-20), ResignationDate = first.AddDays(12), Month = month, Year = year });
                    foreach (var store in new[] { "Store A", "Store B" })
                        db.StoreReferences.Add(new StoreReference
                        {
                            Month = month, Year = year, StoreName = store, StoreLeader = "Leader " + store, OperationConsultant = "OC 1", OperationManager = "OM 1",
                            SeniorOperationConsultant = "SOC 1", OperationDirector = "OD 1",
                        });
                }
                return Task.CompletedTask;
            });
            _seeded = true;
        }
        finally { SeedLock.Release(); }
    }

    private async Task<List<string>> ExportSheetsAsync(string query)
    {
        await EnsureSeededAsync();
        var admin = await _app.AdminClientAsync();
        var response = await admin.GetAsync($"/admin/dashboard/export?reportType=comparisons&{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var wb = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.All(wb.Worksheets, ws => Assert.NotNull(ws.LastRowUsed()));   // every sheet has at least its header
        return wb.Worksheets.Select(w => w.Name).ToList();
    }

    [Theory]
    [InlineData("turnover", "Turnover KPIs|Turnover By Store (A vs B)|Monthly Trend|Teams (A vs B)|Store Movers")]
    [InlineData("breakdown", "By Job Title|By Tenure|By Payroll Group")]
    [InlineData("ninety", "90-Day KPIs|90-Day By Store (A vs B)|Cohort Months|Early Leavers Breakdown|Teams 90-Day")]
    [InlineData("workforce", "Workforce KPIs|Workforce By Store (A vs B)|Composition|Headcount Trend|Teams Headcount")]
    [InlineData("retention", "Retention Milestones|Team Tenure|Stores Retention|Teams Tenure")]
    [InlineData("scorecard", "Scorecard Summary|Flagged By Supervisor|Supervisors Scorecard|Leaders Scorecard")]
    [InlineData("exit", "Exit Reasons|Exit Sentiment|Exit Experience|Exit By Job and Return")]
    [InlineData("drivers", "Engagement Drivers")]
    public async Task Export_BuildsTheSheetsOfEveryTopic(string topic, string expectedSheets)
    {
        var sheets = await ExportSheetsAsync($"topic={topic}&year=2026&months=1,2&yearB=2025&monthsB=1,2");
        foreach (var expected in expectedSheets.Split('|')) Assert.Contains(expected, sheets);
    }

    [Theory]
    [InlineData("turnover")]
    [InlineData("workforce")]
    [InlineData("ninety")]
    [InlineData("retention")]
    [InlineData("exit")]
    public async Task Export_WorksWhenTheSidesDifferInLength_OrAStoreIsPicked(string topic)
    {
        var uneven = await ExportSheetsAsync($"topic={topic}&year=2026&months=1,2&yearB=2025&monthsB=1");
        Assert.NotEmpty(uneven);
        var oneStore = await ExportSheetsAsync($"topic={topic}&year=2026&months=1,2&yearB=2025&monthsB=1,2&store=Store A&storeB=Store A");
        Assert.NotEmpty(oneStore);
        Assert.DoesNotContain("Store Movers", oneStore);   // the per-store views need all stores
    }
}
