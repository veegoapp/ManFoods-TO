using System.Net;
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
}
