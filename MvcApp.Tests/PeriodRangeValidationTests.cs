using System.Net;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Services;
using MvcApp.Tests.TestHelpers;
using Xunit;

namespace MvcApp.Tests;

/// <summary>M3 — month/year/range limits: bad requests get a normal 400, huge ranges never become huge SQL IN lists, normal reporting still works.</summary>
public class PeriodRangeValidationTests : IClassFixture<AppFactory>
{
    private static readonly DateTime Now = new(2026, 6, 15);
    private readonly AppFactory _app;
    public PeriodRangeValidationTests(AppFactory app) => _app = app;

    private static (string Parameter, string Message)? Check(Dictionary<string, object?> args, bool zeroMeansNotProvided = false) =>
        PeriodLimits.Validate(n => args.TryGetValue(n, out var v) ? v : null, zeroMeansNotProvided, Now);

    // ── ExpandRangeKeys ──────────────────────────────────────────────────────

    [Fact]
    public void ExpandRangeKeys_NormalRanges_StillWork()
    {
        Assert.Equal(new[] { 202601 }, DashboardService.ExpandRangeKeys(1, 2026, 1, 2026));
        Assert.Equal(12, DashboardService.ExpandRangeKeys(1, 2026, 12, 2026).Count);
        Assert.Equal(new[] { 202511, 202512, 202601 }, DashboardService.ExpandRangeKeys(11, 2025, 1, 2026));
        Assert.Equal(new[] { 202511, 202512, 202601 }, DashboardService.ExpandRangeKeys(1, 2026, 11, 2025)); // reversed ends are swapped, as before
    }

    [Fact]
    public void ExpandRangeKeys_AllowsTheWholeTenYearLimit_ButNotOneMonthMore()
    {
        Assert.Equal(PeriodLimits.MaxRangeMonths, DashboardService.ExpandRangeKeys(1, 2020, 12, 2029).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardService.ExpandRangeKeys(1, 2020, 1, 2030));
    }

    [Theory]
    [InlineData(0, 2026, 1, 2026)]
    [InlineData(13, 2026, 1, 2026)]
    [InlineData(1, 2026, 14, 2026)]
    [InlineData(1, 1, 1, 2026)]
    [InlineData(1, 2026, 1, 9999)]
    [InlineData(1, 1, 12, 9999)]
    [InlineData(1, int.MaxValue, 1, 2026)]
    public void ExpandRangeKeys_RefusesBadMonthsYearsAndHugeSpans_InsteadOfBuildingThem(int fm, int fy, int tm, int ty) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DashboardService.ExpandRangeKeys(fm, fy, tm, ty));

    // ── the validator ────────────────────────────────────────────────────────

    [Fact]
    public void Validator_AcceptsTheRequestsTheUiActuallySends()
    {
        Assert.Null(Check(new() { ["month"] = 3, ["year"] = 2026 }));
        Assert.Null(Check(new() { ["month"] = (int?)3, ["year"] = (int?)2026, ["fromMonth"] = (int?)1, ["fromYear"] = (int?)2026 }));
        Assert.Null(Check(new() { ["year"] = (int?)2026, ["months"] = "1,3,5,12", ["store"] = "Store A,Store B", ["om"] = "Ahmed" }));
        Assert.Null(Check(new() { ["fromMonth"] = (int?)1, ["fromYear"] = (int?)2026, ["toMonth"] = (int?)6, ["toYear"] = (int?)2026 })); // Retention
        Assert.Null(Check(new() { ["sinceYear"] = (int?)2026, ["yearB"] = (int?)2025, ["monthsB"] = "2,4" }));
        Assert.Null(Check(new() { ["month"] = (int?)null, ["year"] = (int?)null, ["months"] = (string?)null })); // everything optional
        Assert.Null(Check(new() { ["months"] = "", ["store"] = "  " }));
    }

    [Fact]
    public void Validator_AcceptsAFullDataHistoryRange()
    {
        // Go-live is 2026-01; ten years of monthly uploads is the widest span allowed.
        Assert.Null(Check(new() { ["month"] = (int?)12, ["year"] = (int?)2035, ["fromMonth"] = (int?)1, ["fromYear"] = (int?)2026 }));
    }

    [Theory]
    [InlineData("month", 13)]
    [InlineData("month", 0)]
    [InlineData("fromMonth", -1)]
    [InlineData("toMonth", 99)]
    [InlineData("year", 1)]
    [InlineData("year", 1999)]
    [InlineData("year", 2101)]
    [InlineData("fromYear", 0)]
    [InlineData("toYear", 9999)]
    [InlineData("sinceYear", int.MinValue)]
    [InlineData("yearB", 20260)]
    public void Validator_RejectsOutOfRangeMonthsAndYears(string name, int value)
    {
        var error = Check(new() { [name] = value });
        Assert.NotNull(error);
        Assert.Equal(name, error!.Value.Parameter);
    }

    [Fact]
    public void Validator_RejectsRangesLongerThanTheLimit()
    {
        Assert.NotNull(Check(new() { ["month"] = (int?)1, ["year"] = (int?)2100, ["fromMonth"] = (int?)1, ["fromYear"] = (int?)2000 }));
        Assert.NotNull(Check(new() { ["month"] = (int?)1, ["year"] = (int?)2036, ["fromMonth"] = (int?)1, ["fromYear"] = (int?)2026 })); // 121 months
        Assert.NotNull(Check(new() { ["fromYear"] = (int?)2000 })); // open end = now (2026-06): 318 months
        Assert.Null(Check(new() { ["fromYear"] = (int?)2020 })); // open end = now: 78 months
    }

    [Theory]
    [InlineData("1,2,x")]
    [InlineData("13")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,2,3,4,5,6,7,8,9,10,11,12,1")]
    [InlineData("1;2")]
    public void Validator_RejectsMalformedMonthLists(string months)
    {
        Assert.NotNull(Check(new() { ["months"] = months }));
        Assert.NotNull(Check(new() { ["monthsB"] = months }));
    }

    [Fact]
    public void Validator_RejectsOversizedFilterLists()
    {
        Assert.NotNull(Check(new() { ["store"] = string.Join(",", Enumerable.Range(0, PeriodLimits.MaxFilterValues + 1).Select(i => "s" + i)) }));
        Assert.NotNull(Check(new() { ["jobs"] = new string('x', PeriodLimits.MaxFilterValueLength + 1) }));
        Assert.Null(Check(new() { ["store"] = string.Join(",", Enumerable.Range(0, PeriodLimits.MaxFilterValues).Select(i => "s" + i)) }));
    }

    [Fact]
    public void Validator_ExportActions_MayLeaveMonthAndYearAtZero_ButNothingElse()
    {
        Assert.Null(Check(new() { ["month"] = 0, ["year"] = 0 }, zeroMeansNotProvided: true));
        Assert.NotNull(Check(new() { ["month"] = 0, ["year"] = 0 }, zeroMeansNotProvided: false)); // JSON endpoints: 0 is invalid
        Assert.NotNull(Check(new() { ["month"] = 13, ["year"] = 2026 }, zeroMeansNotProvided: true));
        Assert.NotNull(Check(new() { ["fromYear"] = 0 }, zeroMeansNotProvided: true));
    }

    // ── through the real pipeline ────────────────────────────────────────────

    private static async Task<ValidationProblemDetails?> ProblemAsync(HttpResponseMessage response) =>
        System.Text.Json.JsonSerializer.Deserialize<ValidationProblemDetails>(await response.Content.ReadAsStringAsync(),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    [Theory]
    [InlineData("/api/dashboard/kpis?month=13&year=2026", "month")]
    [InlineData("/api/dashboard/kpis?month=1&year=1", "year")]
    [InlineData("/api/dashboard/kpis?month=1&year=2026&fromMonth=1&fromYear=1", "fromYear")]
    [InlineData("/api/dashboard/kpis?year=2026&months=1,2,zz", "months")]
    [InlineData("/api/dashboard/store-comparison?month=1&year=9999", "year")]
    [InlineData("/api/dashboard/turnover-by-job-title?month=0&year=2026", "month")]
    [InlineData("/api/retention/milestones?fromMonth=1&fromYear=2000&toMonth=12&toYear=2100", "fromYear")]
    [InlineData("/api/retention/trend?sinceYear=3", "sinceYear")]
    [InlineData("/api/ninety-day-turnover/kpi?month=1&year=2026", null)] // valid: see below
    [InlineData("/api/ninety-day-turnover/kpi?year=2026", "month")] // required month missing → 0 → rejected, not a 500
    [InlineData("/api/early-warning/watchlist?year=2026&months=99", "months")]
    [InlineData("/api/workforce-planning/summary?year=1&month=1", "year")]
    public async Task BadPeriodRequests_GetANormal400_NotA500(string url, string? badParameter)
    {
        var admin = await _app.AdminClientAsync();

        var response = await admin.GetAsync(url);

        if (badParameter == null) { Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode); return; }
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.NotNull(problem);
        Assert.Contains(badParameter, problem!.Errors.Keys);
    }

    [Theory]
    [InlineData("/api/dashboard/kpis")]
    [InlineData("/api/dashboard/kpis?month=3&year=2026")]
    [InlineData("/api/dashboard/kpis?month=3&year=2026&fromMonth=1&fromYear=2026")]
    [InlineData("/api/dashboard/kpis?year=2026&months=1,3,5&store=A,B&om=X&jobs=Crew")]
    [InlineData("/api/dashboard/store-comparison?month=6&year=2026&fromMonth=1&fromYear=2025")]
    [InlineData("/api/dashboard/available-periods")]
    [InlineData("/api/retention/milestones?fromMonth=1&fromYear=2026&toMonth=6&toYear=2026")]
    [InlineData("/api/retention/trend?sinceYear=2026")]
    [InlineData("/api/early-warning/watchlist?year=2026&months=1,2,3")]
    [InlineData("/api/workforce-planning/summary?year=2026&month=3")]
    public async Task NormalReportingRequests_AreUnaffected(string url)
    {
        var admin = await _app.AdminClientAsync();

        var response = await admin.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExcelExport_RejectsBadMonthAndYear_ButStillAllowsReportsWithoutThem()
    {
        var admin = await _app.AdminClientAsync();

        var bad = await admin.GetAsync("/admin/dashboard/export?reportType=stores-overview&month=13&year=2026");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var hugeRange = await admin.GetAsync("/admin/dashboard/export?reportType=comparisons&year=2026&yearB=1&monthsB=1");
        Assert.Equal(HttpStatusCode.BadRequest, hugeRange.StatusCode);

        // month/year default to 0 for reports that don't take a period — still fine.
        var noPeriod = await admin.GetAsync("/admin/dashboard/export?reportType=turnover");
        Assert.Equal(HttpStatusCode.OK, noPeriod.StatusCode);
        var withPeriod = await admin.GetAsync("/admin/dashboard/export?reportType=stores-overview&month=3&year=2026");
        Assert.Equal(HttpStatusCode.OK, withPeriod.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_StillGetsTheLoginRedirect_NotAValidationAnswer()
    {
        var anonymous = _app.NewClient();

        var response = await anonymous.GetAsync("/api/dashboard/kpis?month=13&year=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }
}
