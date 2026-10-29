using Chaos.Web.Latency;

namespace Chaos.Web.Tests;

public sealed class LatencyRunTests
{
    [Theory]
    [InlineData("google=12,3;aws-us-east-1=40,")]
    [InlineData("chaos=,")]
    [InlineData("a=0,60000")]
    public void ValidRunsRoundTrip(string text)
    {
        Assert.True(LatencyRun.IsValid(text));
        Assert.True(LatencyRun.TryParse(text, out var results));
        Assert.Equal(text, LatencyRun.Format(results));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("google")]
    [InlineData("google=1")]
    [InlineData("google=01,2")]
    [InlineData("Google=1,2")]
    [InlineData("a=1,2;a=3,4")]
    [InlineData("a=60001,1")]
    [InlineData("a=-1,1")]
    [InlineData("a=1,2,3")]
    [InlineData("=1,2")]
    [InlineData("a=1.5,2")]
    public void InvalidRunsAreRejected(string? text) => Assert.False(LatencyRun.IsValid(text));

    [Fact]
    public void SummariesUseMediansAndCountAnswers()
    {
        Assert.True(LatencyRun.TryParse("a=10,1;b=30,;c=,5;d=,", out var results));
        Assert.Equal(new LatencySummary(20, 3, 3, 4), LatencyRun.Summarize(results));
        Assert.Equal(2, LatencyRun.Median([1, 2]));
        Assert.Equal(2, LatencyRun.Median([3, null, 1, 2]));
        Assert.Null(LatencyRun.Median([null]));
    }

    [Theory]
    [InlineData("a=10,1;b=30,;c=,5;d=,", 20, 3)]
    [InlineData("a=5,;b=,;c=,", null, null)]
    [InlineData("a=5,200;b=7,150;c=,", 6, 175)]
    [InlineData("chaos=3,", 3, null)]
    public void RanksNeedHalfTheEndpointsToAnswer(string text, int? browser, int? server)
    {
        Assert.Equal(new LatencyRank(browser, server), LatencyRun.Rank(text));
    }

    [Fact]
    public void InvalidRunsHaveNoRank() => Assert.Equal(LatencyRank.None, LatencyRun.Rank("nope"));

    [Fact]
    public void ComposeSkipsServerTimingsForBrowserOnlyEndpoints()
    {
        LatencyEndpoint[] endpoints = [new("self", "Self", "/time", FromServer: false), new("x", "X", "https://x.example/")];
        Assert.Equal("self=5,;x=6,7", LatencyRun.Compose(endpoints, [5, 6], [9, 7]));
        Assert.Equal("self=,;x=60000,", LatencyRun.Compose(endpoints, [null, 99_999], []));
    }

    [Fact]
    public void TheCatalogIsUsableInRunsAndThePolicy()
    {
        Assert.Equal(LatencyCatalog.Endpoints.Count, LatencyCatalog.Endpoints.Select(endpoint => endpoint.Key).Distinct().Count());
        Assert.True(LatencyCatalog.Endpoints.Count <= LatencyRun.MaxEntries);
        var text = LatencyRun.Compose(LatencyCatalog.Endpoints, [.. LatencyCatalog.Endpoints.Select(_ => (int?)60_000)], [.. LatencyCatalog.Endpoints.Select(_ => (int?)60_000)]);
        Assert.True(LatencyRun.IsValid(text));
        Assert.All(LatencyCatalog.Origins, origin => Assert.StartsWith("https://", origin, StringComparison.Ordinal));
        Assert.Equal("Google", LatencyCatalog.LabelOf("google"));
        Assert.Equal("unknown-key", LatencyCatalog.LabelOf("unknown-key"));
    }
}
