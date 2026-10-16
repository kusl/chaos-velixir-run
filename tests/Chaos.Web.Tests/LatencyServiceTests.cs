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

    private LatencyService Create(ILatencyProbe probe, Action<ChaosOptions>? configure = null) =>
        new(_time, Limits.Create(configure), probe, NullLogger<LatencyService>.Instance);
}
