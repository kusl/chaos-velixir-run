using System.Net;
using Chaos.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Chaos.Web.Tests;

public sealed class TokenBucketTests
{
    [Fact]
    public void TakesUntilEmptyThenRefillsOverTime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var bucket = new TokenBucket(2, 1, time);
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void NeverHoldsMoreThanItsCapacity()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var bucket = new TokenBucket(2, 1, time);
        time.Advance(TimeSpan.FromHours(1));
        Assert.True(bucket.TryTake(2));
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void KeyedBucketsAreIndependentAndBounded()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var buckets = new KeyedTokenBuckets(1, 1, time, maxKeys: 3);
        Assert.True(buckets.TryTake("a"));
        Assert.False(buckets.TryTake("a"));
        Assert.True(buckets.TryTake("b"));
        for (var i = 0; i < 10; i++)
        {
            buckets.TryTake($"k{i}");
        }

        Assert.InRange(buckets.Count, 1, 3);
    }
}

public sealed class ConnectionTrackerTests
{
    [Fact]
    public void LimitsConnectionsPerClientAndOverall()
    {
        var tracker = new ConnectionTracker(Limits.Create(o =>
        {
            o.MaxConnectionsPerClient = 2;
            o.MaxConnections = 3;
        }));

        Assert.Equal(ConnectionAdmission.Admitted, tracker.TryAdmit("a", out var first));
        Assert.Equal(ConnectionAdmission.Admitted, tracker.TryAdmit("a", out var second));
        Assert.Equal(ConnectionAdmission.ClientFull, tracker.TryAdmit("a", out _));
        Assert.Equal(ConnectionAdmission.Admitted, tracker.TryAdmit("b", out var third));
        Assert.Equal(ConnectionAdmission.ServerFull, tracker.TryAdmit("c", out _));

        first?.Dispose();
        first?.Dispose();
        Assert.Equal(2, tracker.Total);
        Assert.Equal(1, tracker.CountFor("a"));
        Assert.Equal(ConnectionAdmission.Admitted, tracker.TryAdmit("c", out var fourth));

        second?.Dispose();
        third?.Dispose();
        fourth?.Dispose();
        Assert.Equal(0, tracker.Total);
    }
}

public sealed class OriginPolicyTests
{
    [Theory]
    [InlineData(null, "chaos.velixir.run", true)]
    [InlineData("https://chaos.velixir.run", "chaos.velixir.run", true)]
    [InlineData("http://localhost:5216", "localhost:5216", true)]
    [InlineData("https://chaos.velixir.run", "internal:8080", true)]
    [InlineData("https://CHAOS.velixir.run/", "internal:8080", true)]
    [InlineData("https://evil.example", "chaos.velixir.run", false)]
    [InlineData("null", "chaos.velixir.run", false)]
    [InlineData("file:///etc", "chaos.velixir.run", false)]
    public void AllowsOnlyThisSite(string? origin, string host, bool expected)
    {
        Assert.Equal(expected, OriginPolicy.IsAllowed(origin, host, ["https://chaos.velixir.run"]));
    }
}

public sealed class ClientAddressTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    public void GroupsAddressesSensibly(string address, string expected)
    {
        Assert.Equal(expected, ClientAddress.Key(IPAddress.Parse(address)));
    }

    [Fact]
    public void MissingAddressesShareOneKey()
    {
        Assert.Equal(ClientAddress.Unknown, ClientAddress.Key(null));
    }
}

public sealed class CookielessAntiforgeryTests
{
    [Fact]
    public void NeverIssuesTokens()
    {
        var tokens = new CookielessAntiforgery().GetAndStoreTokens(new DefaultHttpContext());
        Assert.Null(tokens.RequestToken);
        Assert.Null(tokens.CookieToken);
    }

    [Fact]
    public async Task RejectsUnsafeRequests()
    {
        var antiforgery = new CookielessAntiforgery();
        var get = new DefaultHttpContext();
        get.Request.Method = HttpMethods.Get;
        var post = new DefaultHttpContext();
        post.Request.Method = HttpMethods.Post;

        Assert.True(await antiforgery.IsRequestValidAsync(get));
        Assert.False(await antiforgery.IsRequestValidAsync(post));
        await antiforgery.ValidateRequestAsync(get);
        await Assert.ThrowsAsync<AntiforgeryValidationException>(() => antiforgery.ValidateRequestAsync(post));
    }
}

public sealed class ModulePathTests
{
    [Theory]
    [InlineData("Components/App.razor.js", "./Components/App.razor.js")]
    [InlineData("./Components/App.razor.js", "./Components/App.razor.js")]
    [InlineData("/Components/App.razor.js", "/Components/App.razor.js")]
    public void MakesModulePathsRelative(string asset, string expected)
    {
        Assert.Equal(expected, ModulePath.Of(asset));
    }
}

public sealed class ChaosOptionsTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        Assert.True(new ChaosOptions().IsValid());
    }

    [Fact]
    public void RejectsImpossibleLimits()
    {
        Assert.False(new ChaosOptions { MaxConnections = 1, MaxConnectionsPerClient = 2 }.IsValid());
        Assert.False(new ChaosOptions { BoardCapacity = 0 }.IsValid());
        Assert.False(new ChaosOptions { CanvasMaxPoints = 10 }.IsValid());
    }
}
