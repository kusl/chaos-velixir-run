using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Chaos.Web.Postcards;

namespace Chaos.Web.Tests;

public sealed class PostcardServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _time = new(Start);
    private readonly List<PostcardService> _services = [];

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }
    }

    [Fact]
    public void KeepsOnlyTheNewestPostcard()
    {
        var postcards = Create();
        var first = postcards.Submit("first", [], null, "a");
        _time.Advance(TimeSpan.FromSeconds(1));
        var second = postcards.Submit("second", [TestImages.Png()], null, "b");

        Assert.Equal(PostcardResult.Accepted, first.Result);
        Assert.Equal(PostcardResult.Accepted, second.Result);
        Assert.Equal(second.Card, postcards.Current);
        Assert.False(postcards.TryGetImage(first.Card!.Id, 0, out _, out _));
        Assert.True(postcards.TryGetImage(second.Card!.Id, 0, out var info, out var bytes));
        Assert.Equal("image/png", info!.Type);
        Assert.Equal(info.Length, bytes.Length);
        Assert.False(postcards.TryGetImage(second.Card.Id, 1, out _, out _));
        Assert.Equal(Start.AddSeconds(1), Uuid7.Timestamp(second.Card.Id));
    }

    [Fact]
    public void AllowsLongTextAndCutsAtTheLimit()
    {
        var postcards = Create();
        var reply = postcards.Submit(new string('x', PostcardRules.MaxTextLength + 50), [], null, "a");
        Assert.Equal(PostcardRules.MaxTextLength, reply.Card!.Text.Length);
        Assert.True(PostcardRules.MaxTextLength > MessageText.MaxLength);
    }

    [Fact]
    public void ImagesAloneAreEnough()
    {
        var reply = Create().Submit("  ", [TestImages.Gif()], null, "a");
        Assert.Equal(PostcardResult.Accepted, reply.Result);
        Assert.Equal("", reply.Card!.Text);
        Assert.Equal("image/gif", Assert.Single(reply.Card.Images).Type);
    }

    [Fact]
    public void RejectsEmptyTooManyTooLargeAndBadImages()
    {
        var postcards = Create();
        Assert.Equal(PostcardResult.Empty, postcards.Submit(" \n ", [], null, "a").Result);
        Assert.Equal(PostcardResult.TooManyImages, postcards.Submit("x", [.. Enumerable.Repeat(TestImages.Png(), PostcardRules.MaxImages + 1)], null, "a").Result);
        Assert.Equal(PostcardResult.TooLarge, postcards.Submit("x", [new byte[PostcardRules.MaxImageBytes + 1]], null, "a").Result);
        Assert.Equal(PostcardResult.TooLarge, postcards.Submit("x", [.. Enumerable.Repeat(new byte[(PostcardRules.MaxTotalBytes / 3) + 1], 3)], null, "a").Result);
        Assert.Equal(PostcardResult.BadImage, postcards.Submit("x", [TestImages.Png(), "not an image"u8.ToArray()], null, "a").Result);
        Assert.Null(postcards.Current);
    }

    [Fact]
    public void CheckRunsBeforeAnyUpload()
    {
        Assert.Equal(PostcardResult.Accepted, PostcardService.Check("hi", []));
        Assert.Equal(PostcardResult.Accepted, PostcardService.Check(null, [10]));
        Assert.Equal(PostcardResult.TooLarge, PostcardService.Check("hi", [0]));
        Assert.Equal(PostcardResult.TooLarge, PostcardService.Check("hi", [-1]));
        Assert.Equal(PostcardResult.Empty, PostcardService.Check("​", []));
    }

    [Fact]
    public void IsRateLimitedPerClient()
    {
        var postcards = Create(o =>
        {
            o.PostcardBurst = 1;
            o.PostcardsPerSecond = 0.1;
        });
        Assert.Equal(PostcardResult.Accepted, postcards.Admit("one", [], "a"));
        Assert.Equal(PostcardResult.TooFast, postcards.Admit("two", [], "a"));
        Assert.Equal(PostcardResult.Accepted, postcards.Admit("three", [], "b"));
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(PostcardResult.Accepted, postcards.Admit("four", [], "a"));
    }

    [Fact]
    public void AnInvalidPostcardDoesNotSpendTheRateLimit()
    {
        var postcards = Create(o => o.PostcardBurst = 1);
        Assert.Equal(PostcardResult.Empty, postcards.Admit(" ", [], "a"));
        Assert.Equal(PostcardResult.Accepted, postcards.Admit("now", [], "a"));
    }

    [Fact]
    public void RejectsARepeatOfTheCurrentPostcard()
    {
        var postcards = Create();
        Assert.Equal(PostcardResult.Accepted, postcards.Submit("same", [TestImages.Png()], null, "a").Result);
        Assert.Equal(PostcardResult.Repeat, postcards.Submit("same", [TestImages.Png()], null, "b").Result);
        Assert.Equal(PostcardResult.Accepted, postcards.Submit("same", [], null, "c").Result);
    }

    [Fact]
    public void UploadsAreLimitedAtOnce()
    {
        var postcards = Create(o => o.PostcardUploads = 1);
        var first = postcards.TryBeginUpload();
        Assert.NotNull(first);
        Assert.Null(postcards.TryBeginUpload());
        first.Dispose();
        first.Dispose();
        using var second = postcards.TryBeginUpload();
        Assert.NotNull(second);
    }

    [Fact]
    public void KeepsNormalizedDetails()
    {
        var reply = Create().Submit("located", [], [new("latitude", "27.7"), new("longitude", "85.3"), new("bogus", "x")], "a");
        Assert.Equal(["latitude", "longitude"], reply.Card!.Meta!.Select(entry => entry.Key));
    }

    [Fact]
    public void SubscribersGetTheCurrentPostcardThenEveryChange()
    {
        var postcards = Create();
        var recorder = new PostcardRecorder();
        postcards.Subscribe(recorder);
        var reply = postcards.Submit("hello", [], null, "a");

        Assert.Equal(2, recorder.Updates.Count);
        Assert.Equal("snapshot", recorder.Updates[0].Kind);
        Assert.Null(recorder.Updates[0].Card);
        Assert.Equal("change", recorder.Updates[1].Kind);
        Assert.Equal(recorder.Updates[0].Version + 1, recorder.Updates[1].Version);
        Assert.Equal(reply.Card, recorder.Updates[1].Card);
        Assert.Equal(postcards.Version, recorder.Updates[1].Version);
    }

    [Fact]
    public void SubscribersThatCannotKeepUpAreDropped()
    {
        var postcards = Create();
        var recorder = new PostcardRecorder(accept: false);
        postcards.Subscribe(recorder);
        Assert.False(postcards.IsSubscribed(recorder));
    }

    [Fact]
    public void OfferRestoresAnOlderPostcardAfterARestart()
    {
        var postcards = Create();
        var id = Guid.CreateVersion7(Start.AddDays(-1));
        Assert.True(postcards.WouldRestore(id));
        Assert.True(postcards.Offer(id, "back again", [TestImages.Png()], [new("latitude", "1"), new("longitude", "2")]));
        Assert.Equal(id, postcards.Current!.Id);
        Assert.True(postcards.TryGetImage(id, 0, out _, out _));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-2)), "older", [], null));
        Assert.True(postcards.Offer(Guid.CreateVersion7(Start.AddHours(-1)), "newer", [], null));
    }

    [Fact]
    public void OfferRejectsTheFutureForgeriesAndBadContent()
    {
        var postcards = Create();
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddMinutes(1)), "future", [], null));
        Assert.False(postcards.Offer(Guid.NewGuid(), "v4", [], null));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-1)), " padded", [], null));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-1)), "", [], null));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-1)), "bad image", [[1, 2, 3]], null));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-1)), "bad meta", [], [new("latitude", "1")]));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddMinutes(5)), "forged", [], null));
        Assert.Null(postcards.Current);
    }

    [Fact]
    public void OfferNeverReplacesANewerPostcard()
    {
        var postcards = Create();
        postcards.Submit("live", [], null, "a");
        Assert.False(postcards.WouldRestore(Guid.CreateVersion7(Start.AddDays(-1))));
        Assert.False(postcards.Offer(Guid.CreateVersion7(Start.AddDays(-1)), "old", [], null));
        Assert.Equal("live", postcards.Current!.Text);
    }

    [Fact]
    public void StatusesExplainEveryRefusal()
    {
        foreach (var result in Enum.GetValues<PostcardResult>())
        {
            Assert.Equal(result == PostcardResult.Accepted, PostcardSent.StatusOf(result) is null);
        }
    }

    private PostcardService Create(Action<ChaosOptions>? configure = null)
    {
        var service = new PostcardService(_time, Limits.Create(configure));
        _services.Add(service);
        return service;
    }
}
