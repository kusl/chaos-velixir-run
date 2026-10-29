using Chaos.Web.Infrastructure;
using Chaos.Web.Latency;
using Chaos.Web.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chaos.Web.Tests;

public sealed class LatencyServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Start);

    private static int Count => LatencyCatalog.Endpoints.Count;

    [Fact]
    public async Task ARunCombinesBrowserAndServerTimings()
    {
        using var latency = Create(new FakeProbe(endpoints => [.. endpoints.Select(_ => (int?)7)]));
        var viewer = new LatencyRecorder();
        latency.Subscribe(viewer);

        var start = latency.Begin("a");
        Assert.Equal(StartResult.Accepted, start.Result);
        Assert.True(Uuid7.IsValid(start.Id));
        Assert.True(latency.Running);
        Assert.Equal("state", viewer.Updates[^1].Kind);
        Assert.True(viewer.Updates[^1].Running);

        Assert.True(latency.Finish(start.Id, [.. Enumerable.Repeat(12, Count)]));
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(latency.Running);
        var run = Assert.Single(latency.Messages);
        Assert.Equal(start.Id, run.Id);
        Assert.True(LatencyRun.IsValid(run.Text));
        Assert.True(LatencyRun.TryParse(run.Text, out var results));
        Assert.Equal(Count, results.Count);
        Assert.All(results, result => Assert.Equal(12, result.Browser));
        Assert.Null(results.Single(result => result.Key == "chaos").Server);
        Assert.Equal(7, results.Single(result => result.Key == "google").Server);
        var delta = viewer.Updates[^1];
        Assert.Equal("delta", delta.Kind);
        Assert.False(delta.Running);
    }

    [Fact]
    public async Task OnlyOneRunAtATime()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]), o => o.LatencyGapSeconds = 0);
        var first = latency.Begin("a");
        Assert.Equal(StartResult.Busy, latency.Begin("b").Result);
        latency.Finish(first.Id, [.. Enumerable.Repeat(-1, Count)]);
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(StartResult.Accepted, latency.Begin("b").Result);
    }

    [Fact]
    public async Task RunsAreSpacedOutAndEachClientWaitsItsTurn()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]));
        var first = latency.Begin("a");
        latency.Finish(first.Id, [.. Enumerable.Repeat(-1, Count)]);
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(StartResult.Busy, latency.Begin("b").Result);
        _time.Advance(TimeSpan.FromSeconds(10));
        var again = latency.Begin("a");
        Assert.Equal(StartResult.Wait, again.Result);
        Assert.Equal("wait 290", again.Reply);
        var other = latency.Begin("b");
        Assert.Equal(StartResult.Accepted, other.Result);
        Assert.StartsWith($"ok {other.Id:D} 300", other.Reply, StringComparison.Ordinal);
        latency.Finish(other.Id, [.. Enumerable.Repeat(-1, Count)]);
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromSeconds(290));
        Assert.Equal(StartResult.Accepted, latency.Begin("a").Result);
    }

    [Fact]
    public void FinishChecksTheRunAndTheValues()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]));
        var start = latency.Begin("a");
        Assert.False(latency.Finish(Guid.CreateVersion7(Start), [.. Enumerable.Repeat(1, Count)]));
        Assert.False(latency.Finish(start.Id, [1, 2]));
        Assert.False(latency.Finish(start.Id, [.. Enumerable.Repeat(-2, Count)]));
        Assert.False(latency.Finish(start.Id, [.. Enumerable.Repeat(LatencyRun.MaxMilliseconds + 1, Count)]));
        Assert.True(latency.Finish(start.Id, [.. Enumerable.Repeat(1, Count)]));
        Assert.False(latency.Finish(start.Id, [.. Enumerable.Repeat(1, Count)]));
    }

    [Fact]
    public async Task TheServerProbeCanBeTurnedOff()
    {
        var probe = new FakeProbe(endpoints => [.. endpoints.Select(_ => (int?)1)]);
        using var latency = Create(probe, o => o.LatencyServerProbe = false);
        var start = latency.Begin("a");
        latency.Finish(start.Id, [.. Enumerable.Repeat(5, Count)]);
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, probe.Calls);
        Assert.True(LatencyRun.TryParse(Assert.Single(latency.Messages).Text, out var results));
        Assert.All(results, result => Assert.Null(result.Server));
    }

    [Fact]
    public void OffersAreValidatedLikeBoardMessages()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]));
        var old = Guid.CreateVersion7(Start.AddMinutes(-1));
        Assert.Equal(1, latency.Offer([new BoardMessage(old, "google=12,3")]));
        Assert.Equal(0, latency.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-2)), "google=012,3")]));
        Assert.Equal(0, latency.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(1)), "google=12,3")]));
        Assert.Equal(0, latency.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-3)), "hello")]));
    }

    [Fact]
    public void SnapshotsSayWhetherARunIsGoing()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]));
        latency.Begin("a");
        var late = new LatencyRecorder();
        latency.Subscribe(late);
        Assert.True(Assert.Single(late.Updates).Running);
    }

    [Fact]
    public async Task AMeasuredRunJoinsTheLeaderboards()
    {
        using var latency = Create(new FakeProbe(endpoints => [.. endpoints.Select(_ => (int?)150)]));
        var viewer = new LatencyRecorder();
        latency.Subscribe(viewer);
        Assert.Empty(viewer.Updates[0].Boards!.Runs);

        var start = latency.Begin("a");
        latency.Finish(start.Id, [.. Enumerable.Repeat(8, Count)]);
        await latency.WhenIdle().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var leaders = latency.Leaders;
        Assert.Equal(start.Id, Assert.Single(leaders.FastJackpots).Id);
        Assert.Equal(start.Id, Assert.Single(leaders.SlowJackpots).Id);
        Assert.Single(leaders.BrowserFastest);
        Assert.Single(leaders.ServerSlowest);
        var boards = viewer.Updates[^1].Boards!;
        Assert.Equal(start.Id, Assert.Single(boards.Runs).Id);
        Assert.Equal(new[] { start.Id }, boards.BrowserSlowest);
    }

    [Fact]
    public void LeaderOffersFillTheBoardsButNotTheResults()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]), o => o.LatencyRankCapacity = 2);
        var viewer = new LatencyRecorder();
        latency.Subscribe(viewer);
        var offered = Enumerable.Range(1, 5)
            .Select(i => new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-i)), $"a={i * 10},;b=,"))
            .ToList();

        Assert.Equal(4, latency.OfferLeaders(offered));
        Assert.Empty(latency.Messages);
        var leaders = latency.Leaders;
        Assert.Equal(new[] { offered[0].Id, offered[1].Id }, leaders.BrowserFastest.Select(run => run.Id));
        Assert.Equal(new[] { offered[4].Id, offered[3].Id }, leaders.BrowserSlowest.Select(run => run.Id));
        Assert.Empty(leaders.ServerFastest);
        Assert.Empty(leaders.FastJackpots);
        Assert.Empty(leaders.SlowJackpots);
        Assert.Equal(0, latency.OfferLeaders(offered));
        Assert.Equal(4, viewer.Updates[^1].Boards!.Runs.Count);
    }

    [Fact]
    public void RunsWithoutAQuorumAreNotRanked()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]));
        Assert.Equal(0, latency.OfferLeaders([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-1)), "a=1,;b=,;c=,")]));
        Assert.Equal(1, latency.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-2)), "a=1,;b=,;c=,")]));
        Assert.Empty(latency.Leaders.BrowserFastest);
        Assert.Empty(latency.Leaders.FastJackpots);
    }

    [Fact]
    public void JackpotsKeepTheNewestAndRanksBreakTiesByAge()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]), o => o.LatencyJackpotCapacity = 2);
        var older = new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-3)), "a=5,");
        var old = new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-2)), "a=5,");
        var recent = new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-1)), "a=9,");
        latency.OfferLeaders([old, recent, older]);
        Assert.Equal(new[] { recent.Id, old.Id }, latency.Leaders.FastJackpots.Select(run => run.Id));
        Assert.Equal(new[] { older.Id, old.Id, recent.Id }, latency.Leaders.BrowserFastest.Select(run => run.Id));
    }

    [Fact]
    public void LeaderOffersAreLimitedPerCall()
    {
        using var latency = Create(new FakeProbe(endpoints => new int?[endpoints.Count]), o => o.LatencyRankCapacity = 100);
        var offered = Enumerable.Range(1, LatencyService.MaxLeaderOffers + 5)
            .Select(i => new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(-i)), $"a={i},"));
        Assert.Equal(LatencyService.MaxLeaderOffers, latency.OfferLeaders(offered));
    }

    private LatencyService Create(ILatencyProbe probe, Action<ChaosOptions>? configure = null) =>
        new(_time, Limits.Create(configure), probe, NullLogger<LatencyService>.Instance);
}
