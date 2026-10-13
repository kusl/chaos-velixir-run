using Chaos.Web.Drawing;
using Chaos.Web.Infrastructure;

namespace Chaos.Web.Tests;

public sealed class CanvasServiceTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void StrokesReachEveryoneButTheArtist()
    {
        var canvas = Create();
        var artist = new CanvasRecorder();
        var viewer = new CanvasRecorder();
        canvas.Subscribe(artist);
        canvas.Subscribe(viewer);
        var session = canvas.OpenSession(artist);

        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 1, [10, 10, 20, 20], false));
        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 1, [30, 30], true));

        Assert.Single(artist.Updates);
        Assert.Equal(3, viewer.Updates.Count);
        Assert.Equal(0, viewer.Updates[1].Offset);
        Assert.Equal(2, viewer.Updates[2].Offset);
        Assert.True(viewer.Updates[2].Done);
        Assert.Equal(3, canvas.PointCount);
    }

    [Fact]
    public void StaleEpochsAreRejected()
    {
        var canvas = Create();
        var session = canvas.OpenSession(new CanvasRecorder());
        Assert.Equal(DrawResult.Stale, canvas.Draw(session, canvas.Epoch + 1, 1, [1, 1], true));
    }

    [Theory]
    [InlineData(new[] { 4096, 1 })]
    [InlineData(new[] { -1, 1 })]
    [InlineData(new[] { 1, 1, 1 })]
    [InlineData(new int[0])]
    public void InvalidBatchesAreRejected(int[] points)
    {
        var canvas = Create();
        var session = canvas.OpenSession(new CanvasRecorder());
        Assert.Equal(DrawResult.Invalid, canvas.Draw(session, canvas.Epoch, 1, points, false));
    }

    [Fact]
    public void OversizedBatchesAreRejected()
    {
        var canvas = Create();
        var session = canvas.OpenSession(new CanvasRecorder());
        var points = new int[(CanvasService.MaxBatchPoints + 1) * 2];
        Assert.Equal(DrawResult.Invalid, canvas.Draw(session, canvas.Epoch, 1, points, false));
    }

    [Fact]
    public void StrokeIdsMustMoveForward()
    {
        var canvas = Create();
        var session = canvas.OpenSession(new CanvasRecorder());
        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 5, [1, 1], true));
        Assert.Equal(DrawResult.Invalid, canvas.Draw(session, canvas.Epoch, 5, [2, 2], true));
        Assert.Equal(DrawResult.Invalid, canvas.Draw(session, canvas.Epoch, 4, [2, 2], true));
    }

    [Fact]
    public void PointsAreRateLimited()
    {
        var canvas = Create(o =>
        {
            o.StrokePointBurst = 300;
            o.StrokePointsPerSecond = 1;
        });
        var session = canvas.OpenSession(new CanvasRecorder());
        var batch = new int[CanvasService.MaxBatchPoints * 2];
        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 1, batch, false));
        Assert.Equal(DrawResult.TooFast, canvas.Draw(session, canvas.Epoch, 1, batch, false));
    }

    [Fact]
    public void OverlongStrokesEnd()
    {
        var canvas = Create(o => o.StrokePointBurst = 100_000);
        var session = canvas.OpenSession(new CanvasRecorder());
        var batch = new int[CanvasService.MaxBatchPoints * 2];
        for (var i = 0; i < CanvasService.MaxStrokePoints / CanvasService.MaxBatchPoints; i++)
        {
            Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 1, batch, false));
        }

        Assert.Equal(DrawResult.TooLong, canvas.Draw(session, canvas.Epoch, 1, batch, false));
        Assert.Null(session.Open);
    }

    [Fact]
    public void OldFinishedStrokesMakeRoomForNewOnes()
    {
        var canvas = Create(o => o.CanvasMaxPoints = 10);
        var session = canvas.OpenSession(new CanvasRecorder());
        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 1, Points(6), true));
        Assert.Equal(DrawResult.Accepted, canvas.Draw(session, canvas.Epoch, 2, Points(6), true));
        Assert.Equal(1, canvas.StrokeCount);
        Assert.Equal(6, canvas.PointCount);
    }

    [Fact]
    public void OpenStrokesAreNeverEvicted()
    {
        var canvas = Create(o => o.CanvasMaxPoints = 10);
        var first = canvas.OpenSession(new CanvasRecorder());
        var second = canvas.OpenSession(new CanvasRecorder());
        Assert.Equal(DrawResult.Accepted, canvas.Draw(first, canvas.Epoch, 1, Points(6), false));
        Assert.Equal(DrawResult.Full, canvas.Draw(second, canvas.Epoch, 1, Points(6), false));
    }

    [Fact]
    public void ResetStartsANewEpochForEveryone()
    {
        var canvas = Create(o => o.ResetBurst = 10);
        var viewer = new CanvasRecorder();
        canvas.Subscribe(viewer);
        var session = canvas.OpenSession(viewer);
        canvas.Draw(session, canvas.Epoch, 1, Points(2), false);
        var epoch = canvas.Epoch;

        Assert.Equal(ResetResult.Accepted, canvas.Reset("a"));
        Assert.Equal(epoch + 1, canvas.Epoch);
        Assert.Equal(0, canvas.StrokeCount);
        Assert.Equal(0, canvas.PointCount);
        Assert.Equal("reset", viewer.Updates[^1].Kind);
        Assert.Equal(DrawResult.Stale, canvas.Draw(session, epoch, 1, Points(2), true));
    }

    [Fact]
    public void ResetsHaveACooldown()
    {
        var canvas = Create(o => o.ResetBurst = 10);
        Assert.Equal(ResetResult.Accepted, canvas.Reset("a"));
        Assert.Equal(ResetResult.TooFast, canvas.Reset("b"));
        _time.Advance(CanvasService.ResetCooldown);
        Assert.Equal(ResetResult.Accepted, canvas.Reset("b"));
    }

    [Fact]
    public void LateJoinersGetEveryStroke()
    {
        var canvas = Create();
        var session = canvas.OpenSession(new CanvasRecorder());
        canvas.Draw(session, canvas.Epoch, 1, [1, 2, 3, 4], true);
        canvas.Draw(session, canvas.Epoch, 2, [5, 6], false);
        var late = new CanvasRecorder();
        canvas.Subscribe(late);

        var snapshot = Assert.Single(late.Updates);
        Assert.Equal("snapshot", snapshot.Kind);
        Assert.NotNull(snapshot.Strokes);
        Assert.Equal(2, snapshot.Strokes.Count);
        Assert.Equal([1, 2, 3, 4], snapshot.Strokes[0].Points);
        Assert.True(snapshot.Strokes[0].Done);
        Assert.False(snapshot.Strokes[1].Done);
    }

    [Fact]
    public void AbandonFinishesTheOpenStroke()
    {
        var canvas = Create();
        var artist = new CanvasRecorder();
        var viewer = new CanvasRecorder();
        canvas.Subscribe(viewer);
        var session = canvas.OpenSession(artist);
        canvas.Draw(session, canvas.Epoch, 1, Points(2), false);
        canvas.Abandon(session);

        var last = viewer.Updates[^1];
        Assert.True(last.Done);
        Assert.Empty(last.Points);
        Assert.Equal(2, last.Offset);
    }

    private CanvasService Create(Action<ChaosOptions>? configure = null) => new(_time, Limits.Create(configure));

    private static int[] Points(int count) => [.. Enumerable.Range(0, count * 2).Select(i => i % 4096)];
}
