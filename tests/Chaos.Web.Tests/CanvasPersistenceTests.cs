using Chaos.Web.Drawing;
using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class CanvasPersistenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Start);

    [Fact]
    public void StrokesAndResetsGetIncreasingIds()
    {
        var canvas = Create(o => o.ResetBurst = 10);
        var session = canvas.OpenSession(new CanvasRecorder());
        canvas.Draw(session, canvas.Epoch, 1, [1, 1], true);
        var first = session.LastStroke;
        canvas.Draw(session, canvas.Epoch, 2, [1, 1], true);
        var second = session.LastStroke;
        Assert.True(Uuid7.IsValid(first));
        Assert.True(second.CompareTo(first) > 0);

        Assert.Equal(ResetResult.Accepted, canvas.Reset("a"));
        Assert.True(canvas.ResetId.CompareTo(second) > 0);
        var next = canvas.OpenSession(new CanvasRecorder());
        canvas.Draw(next, canvas.Epoch, 1, [1, 1], true);
        Assert.True(next.LastStroke.CompareTo(canvas.ResetId) > 0);
    }

    [Fact]
    public void ResetsAreBroadcastWithTheirId()
    {
        var canvas = Create();
        var viewer = new CanvasRecorder();
        canvas.Subscribe(viewer);
        canvas.Reset("a");
        var update = viewer.Updates[^1];
        Assert.Equal("reset", update.Kind);
        Assert.Equal(canvas.ResetId, update.Reset);
    }

    [Fact]
    public void SnapshotsCarryTheRestoreContext()
    {
        var canvas = Create(o => o.CanvasMaxPoints = 5000);
        var session = canvas.OpenSession(new CanvasRecorder());
        canvas.Draw(session, canvas.Epoch, 1, Points(10), true);
        var late = new CanvasRecorder();
        canvas.Subscribe(late);
        var snapshot = Assert.Single(late.Updates);
        Assert.Null(snapshot.Reset);
        Assert.Equal(Start.ToUnixTimeMilliseconds(), snapshot.Started);
        Assert.Equal(4990, snapshot.Room);
    }

    [Fact]
    public void StrokesFromBeforeTheRestartAreTakenBack()
    {
        var canvas = Create();
        var viewer = new CanvasRecorder();
        canvas.Subscribe(viewer);
        var old = Guid.CreateVersion7(Start.AddMinutes(-5));

        Assert.Equal(1, canvas.OfferStrokes([new StrokeOffer(old, [1, 2, 3, 4])]));
        Assert.Equal(1, canvas.StrokeCount);
        Assert.Equal(2, canvas.PointCount);
        var merge = viewer.Updates[^1];
        Assert.Equal("merge", merge.Kind);
        Assert.Equal(old, Assert.Single(merge.Strokes!).Id);
        Assert.Equal(0, canvas.OfferStrokes([new StrokeOffer(old, [1, 2, 3, 4])]));
    }

    [Fact]
    public void StrokesFromThisRunOrTheFutureAreRefused()
    {
        var canvas = Create();
        _time.Advance(TimeSpan.FromMinutes(1));
        var recent = Guid.CreateVersion7(Start.AddSeconds(1));
        var future = Guid.CreateVersion7(Start.AddHours(1));
        Assert.Equal(0, canvas.OfferStrokes([new StrokeOffer(recent, [1, 1]), new StrokeOffer(future, [1, 1]), new StrokeOffer(Guid.NewGuid(), [1, 1])]));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2, 3 })]
    [InlineData(new[] { 4096, 1 })]
    [InlineData(new[] { -1, 1 })]
    public void MalformedStrokesAreRefused(int[] points)
    {
        var canvas = Create();
        Assert.Equal(0, canvas.OfferStrokes([new StrokeOffer(Guid.CreateVersion7(Start.AddMinutes(-1)), points)]));
    }

    [Fact]
    public void OffersNeverEvictAndPreferTheNewest()
    {
        var canvas = Create(o => o.CanvasMaxPoints = CanvasService.MaxStrokePoints);
        var older = Guid.CreateVersion7(Start.AddMinutes(-2));
        var newer = Guid.CreateVersion7(Start.AddMinutes(-1));
        var accepted = canvas.OfferStrokes([new StrokeOffer(older, Points(1500)), new StrokeOffer(newer, Points(1500))]);
        Assert.Equal(1, accepted);
        var late = new CanvasRecorder();
        canvas.Subscribe(late);
        Assert.Equal(newer, Assert.Single(late.Updates[0].Strokes!).Id);
    }

    [Fact]
    public void ARememberedResetDropsOlderStrokes()
    {
        var canvas = Create();
        var viewer = new CanvasRecorder();
        canvas.Subscribe(viewer);
        var before = Guid.CreateVersion7(Start.AddMinutes(-3));
        var reset = Guid.CreateVersion7(Start.AddMinutes(-2));
        var after = Guid.CreateVersion7(Start.AddMinutes(-1));
        canvas.OfferStrokes([new StrokeOffer(before, [1, 1]), new StrokeOffer(after, [2, 2])]);
        var epoch = canvas.Epoch;

        Assert.True(canvas.OfferReset(reset));
        Assert.Equal(reset, canvas.ResetId);
        Assert.Equal(epoch + 1, canvas.Epoch);
        var snapshot = viewer.Updates[^1];
        Assert.Equal("snapshot", snapshot.Kind);
        Assert.Equal(after, Assert.Single(snapshot.Strokes!).Id);
        Assert.Equal(0, canvas.OfferStrokes([new StrokeOffer(before, [1, 1])]));
        Assert.False(canvas.OfferReset(before));
        Assert.False(canvas.OfferReset(reset));
    }

    [Fact]
    public void ResetsFromThisRunWinOverRememberedOnes()
    {
        var canvas = Create();
        _time.Advance(TimeSpan.FromSeconds(5));
        canvas.Reset("a");
        Assert.False(canvas.OfferReset(Guid.CreateVersion7(Start.AddMinutes(-1))));
        Assert.Equal(0, canvas.OfferStrokes([new StrokeOffer(Guid.CreateVersion7(Start.AddMinutes(-1)), [1, 1])]));
    }

    [Fact]
    public void ResetsFromAfterTheRestartAreRefused()
    {
        var canvas = Create();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(canvas.OfferReset(Guid.CreateVersion7(Start.AddSeconds(10))));
        Assert.False(canvas.OfferReset(Guid.NewGuid()));
    }

    [Fact]
    public void DrawRepliesCarryTheStrokeId()
    {
        var id = Guid.CreateVersion7(Start);
        Assert.Equal($"0 {id:D}", DrawReply.Format(DrawResult.Accepted, id));
        Assert.Equal("2", DrawReply.Format(DrawResult.TooFast, id));
        Assert.Equal("0", DrawReply.Format(DrawResult.Accepted, Guid.Empty));
    }

    private CanvasService Create(Action<ChaosOptions>? configure = null) => new(_time, Limits.Create(configure));

    private static int[] Points(int count) => [.. Enumerable.Range(0, count * 2).Select(i => i % 4096)];
}
