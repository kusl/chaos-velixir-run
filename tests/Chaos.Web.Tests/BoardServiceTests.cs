using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class BoardServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Start);

    [Fact]
    public void PostStampsMessagesWithTheServerClock()
    {
        var board = Create();
        Assert.Equal(PostResult.Accepted, board.Post("  hello  ", "a"));
        var message = Assert.Single(board.Messages);
        Assert.Equal("hello", message.Text);
        Assert.Equal(Start, Uuid7.Timestamp(message.Id));
    }

    [Fact]
    public void PostRejectsBlankText()
    {
        Assert.Equal(PostResult.Empty, Create().Post(" \n ", "a"));
    }

    [Fact]
    public void PostIsRateLimitedPerClient()
    {
        var board = Create(o =>
        {
            o.PostBurst = 2;
            o.PostsPerSecond = 0.5;
        });
        Assert.Equal(PostResult.Accepted, board.Post("one", "a"));
        Assert.Equal(PostResult.Accepted, board.Post("two", "a"));
        Assert.Equal(PostResult.TooFast, board.Post("three", "a"));
        Assert.Equal(PostResult.Accepted, board.Post("four", "b"));
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PostResult.Accepted, board.Post("five", "a"));
    }

    [Fact]
    public void PostRejectsRecentRepeats()
    {
        var board = Create();
        Assert.Equal(PostResult.Accepted, board.Post("same", "a"));
        Assert.Equal(PostResult.Repeat, board.Post("same", "b"));
    }

    [Fact]
    public void KeepsOnlyTheNewestMessages()
    {
        var board = Create(o => o.BoardCapacity = 3);
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(PostResult.Accepted, board.Post($"m{i}", $"c{i}"));
        }

        Assert.Equal(["m4", "m3", "m2"], board.Messages.Select(m => m.Text));
    }

    [Fact]
    public void SubscribersGetASnapshotThenDeltas()
    {
        var board = Create(o => o.BoardCapacity = 1);
        board.Post("first", "a");
        var recorder = new BoardRecorder();
        board.Subscribe(recorder);
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Post("second", "b");

        Assert.Equal(2, recorder.Updates.Count);
        var snapshot = recorder.Updates[0];
        var delta = recorder.Updates[1];
        Assert.Equal("snapshot", snapshot.Kind);
        Assert.Equal("first", Assert.Single(snapshot.Added).Text);
        Assert.Equal("delta", delta.Kind);
        Assert.Equal(snapshot.Version, delta.Previous);
        Assert.Equal(snapshot.Version + 1, delta.Version);
        Assert.Equal("second", Assert.Single(delta.Added).Text);
        Assert.Equal(snapshot.Added[0].Id, Assert.Single(delta.Removed));
    }

    [Fact]
    public void SubscribersThatCannotKeepUpAreDropped()
    {
        var board = Create();
        var recorder = new BoardRecorder(accept: false);
        board.Subscribe(recorder);
        Assert.False(board.IsSubscribed(recorder));
    }

    [Fact]
    public void OfferAcceptsOlderMessagesWhenThereIsRoom()
    {
        var board = Create();
        Assert.Equal(2, board.Offer([Old(-2, "older"), Old(-1, "newer")]));
        Assert.Equal(["newer", "older"], board.Messages.Select(m => m.Text));
    }

    [Fact]
    public void OfferRejectsMessagesFromTheFuture()
    {
        var board = Create();
        Assert.Equal(0, board.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(1)), "future")]));
        Assert.Empty(board.Messages);
    }

    [Fact]
    public void OfferRejectsMessagesThisServerShouldAlreadyHave()
    {
        var board = Create();
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, board.Offer([new BoardMessage(Guid.CreateVersion7(Start.AddMinutes(5)), "forged")]));
        Assert.Equal(0, board.Offer([new BoardMessage(Guid.CreateVersion7(Start), "at start")]));
    }

    [Fact]
    public void OfferRejectsInvalidIdsAndText()
    {
        var board = Create();
        Assert.Equal(0, board.Offer(
        [
            new BoardMessage(Guid.NewGuid(), "v4"),
            Old(-1, " padded"),
            Old(-2, ""),
            Old(-3, new string('x', MessageText.MaxLength + 1)),
        ]));
    }

    [Fact]
    public void OfferDoesNothingWhenTheServerIsFullOfNewerMessages()
    {
        var board = Create(o => o.BoardCapacity = 2);
        board.Post("a", "a");
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Post("b", "b");
        var recorder = new BoardRecorder();
        board.Subscribe(recorder);
        Assert.Equal(0, board.Offer([Old(-1, "old")]));
        Assert.Single(recorder.Updates);
        Assert.Equal(["b", "a"], board.Messages.Select(m => m.Text));
    }

    [Fact]
    public void OfferKeepsTheServerCopyOnTies()
    {
        var board = Create();
        var id = Old(-1, "x").Id;
        Assert.Equal(1, board.Offer([new BoardMessage(id, "kept")]));
        Assert.Equal(0, board.Offer([new BoardMessage(id, "ignored")]));
        Assert.Equal("kept", Assert.Single(board.Messages).Text);
    }

    [Fact]
    public void OfferConsidersAtMostFiftyMessages()
    {
        var board = Create(o => o.BoardCapacity = 100);
        var offered = Enumerable.Range(1, 60).Select(i => Old(-i, $"m{i}")).ToList();
        Assert.Equal(BoardService.MaxOffers, board.Offer(offered));
    }

    private BoardService Create(Action<ChaosOptions>? configure = null) => new(_time, Limits.Create(configure));

    private static BoardMessage Old(int minutes, string text) =>
        new(Guid.CreateVersion7(Start.AddMinutes(minutes)), text);
}
