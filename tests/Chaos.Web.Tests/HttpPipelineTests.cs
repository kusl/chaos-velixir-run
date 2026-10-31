using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chaos.Web.Infrastructure;
using Chaos.Web.Latency;
using Chaos.Web.Messages;
using Chaos.Web.Postcards;
using Chaos.Web.Slots;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chaos.Web.Tests;

public sealed class ChaosFactory : WebApplicationFactory<Program>
{
    public const string AllowedOrigin = "https://chaos.example";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("Chaos:AllowedOrigins:0", AllowedOrigin);
    }
}

public sealed class HttpPipelineTests(ChaosFactory factory) : IClassFixture<ChaosFactory>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/", "Chaos")]
    [InlineData("/board", "Chaos board")]
    [InlineData("/postcard", "Chaos postcard")]
    [InlineData("/canvas", "Chaos canvas")]
    [InlineData("/slots", "Chaos slots")]
    [InlineData("/latency", "Chaos latency")]
    [InlineData("/about", "About chaos")]
    public async Task PagesRenderWithoutSettingCookies(string path, string title)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains($"<title>{title}</title>", await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task BoardPageRendersServerMessages()
    {
        var text = $"rendered {Guid.NewGuid():N}";
        Assert.Equal(PostResult.Accepted, factory.Services.GetRequiredService<BoardService>().Post(text, "integration"));
        using var client = CreateClient();
        Assert.Contains(text, await client.GetStringAsync("/board", Cancellation));
    }

    [Fact]
    public async Task BoardPageRendersMessageDetailsCollapsed()
    {
        var text = $"located {Guid.NewGuid():N}";
        var reply = factory.Services.GetRequiredService<BoardService>().Submit(text, $"integration {Guid.NewGuid():N}", [new("latitude", "27.7172"), new("longitude", "85.324"), new("timeZone", "Asia/Kathmandu")]);
        Assert.Equal(PostResult.Accepted, reply.Result);
        using var client = CreateClient();
        var html = await client.GetStringAsync("/board", Cancellation);
        var start = html.IndexOf($"data-id=\"{reply.Message!.Id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var item = html[start..html.IndexOf("</li>", start, StringComparison.Ordinal)];
        var details = Regex.Match(item, "<details class=\"meta\"[^>]*>");
        Assert.True(details.Success);
        Assert.DoesNotContain(" open", details.Value, StringComparison.Ordinal);
        Assert.Matches(">Latitude</dt><dd[^>]*>27.7172</dd>", item);
        Assert.Matches(">Time zone</dt><dd[^>]*>Asia/Kathmandu</dd>", item);
        Assert.Contains("id=\"compose-share\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostcardPageRendersTheCurrentPostcardAndServesItsImages()
    {
        var postcards = factory.Services.GetRequiredService<PostcardService>();
        var text = $"postcard {Guid.NewGuid():N}";
        var reply = postcards.Submit(text, [TestImages.Png(4, 3, "tEXt")], null, $"integration {Guid.NewGuid():N}");
        Assert.Equal(PostcardResult.Accepted, reply.Result);
        var id = reply.Card!.Id;
        using var client = CreateClient();

        var html = await client.GetStringAsync("/postcard", Cancellation);
        Assert.Contains(text, html, StringComparison.Ordinal);
        Assert.Contains($"src=\"postcard/images/{id}/0\" width=\"4\" height=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"postcard-files\"", html, StringComparison.Ordinal);

        using var image = await client.GetAsync($"/postcard/images/{id}/0", Cancellation);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.True(image.Headers.CacheControl?.Private);
        Assert.Equal(TestImages.Png(4, 3), await image.Content.ReadAsByteArrayAsync(Cancellation));

        using var missingIndex = await client.GetAsync($"/postcard/images/{id}/1", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, missingIndex.StatusCode);
        using var missingCard = await client.GetAsync($"/postcard/images/{Guid.NewGuid()}/0", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, missingCard.StatusCode);
    }

    [Fact]
    public async Task LatencyPageListsJackpotsLast()
    {
        using var client = CreateClient();
        var html = await client.GetStringAsync("/latency", Cancellation);
        var results = html.IndexOf("id=\"runs-title\"", StringComparison.Ordinal);
        var leaders = html.IndexOf("id=\"leaders-title\"", StringComparison.Ordinal);
        var jackpots = html.IndexOf("id=\"jackpots-title\"", StringComparison.Ordinal);
        Assert.True(results >= 0 && results < leaders && leaders < jackpots);
    }

    [Fact]
    public async Task SlotsPageRendersSettledResults()
    {
        var slots = factory.Services.GetRequiredService<SlotService>();
        var press = slots.Press($"integration {Guid.NewGuid():N}");
        Assert.Equal(PressResult.Accepted, press.Result);
        using var client = CreateClient();
        var html = await client.GetStringAsync("/slots", Cancellation);
        Assert.Contains("id=\"slots-feed\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-id=\"{press.Id}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlotsPageRendersWinnersWithTheirOdds()
    {
        var slots = factory.Services.GetRequiredService<SlotService>();
        var winner = new BoardMessage(Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(-1)), "QQQQ7");
        slots.OfferWinners([winner]);
        using var client = CreateClient();
        var html = await client.GetStringAsync("/slots", Cancellation);
        Assert.Contains("id=\"slots-winners\"", html, StringComparison.Ordinal);
        Assert.Contains("data-text=\"QQQQ7\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlotsPageRendersJackpots()
    {
        var slots = factory.Services.GetRequiredService<SlotService>();
        var jackpot = new BoardMessage(Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(-30)), "KKKKK");
        Assert.Equal(1, slots.OfferJackpots([jackpot]));
        using var client = CreateClient();
        var html = await client.GetStringAsync("/slots", Cancellation);
        Assert.Contains("id=\"slots-jackpots\"", html, StringComparison.Ordinal);
        Assert.Contains($"class=\"msg win jackpot\" data-id=\"{jackpot.Id}\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"confetti\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponsesCarrySecurityHeaders()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/", Cancellation);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(SecurityHeaders.PermissionsPolicy, response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Contains("geolocation=(self)", SecurityHeaders.PermissionsPolicy, StringComparison.Ordinal);
        Assert.Contains("camera=()", SecurityHeaders.PermissionsPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagesShowTheServerClock()
    {
        using var client = CreateClient();
        var html = await client.GetStringAsync("/about", Cancellation);
        Assert.Contains("id=\"server-clock\"", html, StringComparison.Ordinal);
        Assert.Contains("data-now=\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"announcer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeEndpointReportsTheServerClock()
    {
        using var client = CreateClient();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var response = await client.GetAsync("/time", Cancellation);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var now = json.RootElement.GetProperty("unixMs").GetInt64();
        Assert.InRange(now, before, after);
        Assert.InRange(json.RootElement.GetProperty("offsetMinutes").GetInt32(), -14 * 60, 14 * 60);
    }

    [Fact]
    public async Task LatencyPageListsEveryEndpoint()
    {
        using var client = CreateClient();
        var html = await client.GetStringAsync("/latency", Cancellation);
        foreach (var endpoint in LatencyCatalog.Endpoints)
        {
            Assert.Contains($"data-key=\"{endpoint.Key}\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LatencyPageHasEmptyLeaderboardsBelowTheResults()
    {
        using var client = CreateClient();
        var html = await client.GetStringAsync("/latency", Cancellation);
        var feed = html.IndexOf("id=\"latency-feed\"", StringComparison.Ordinal);
        Assert.True(feed > 0);
        foreach (var id in new[] { "latency-jackpot-winners", "latency-jackpot-losers", "latency-browser-fastest", "latency-browser-slowest", "latency-server-fastest", "latency-server-slowest" })
        {
            Assert.True(html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal) > feed, id);
            Assert.Contains($"id=\"{id}-empty\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ThePolicyAllowsTheLatencyEndpoints()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/", Cancellation);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.StartsWith("default-src 'self'; script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("https://dynamodb.us-east-1.amazonaws.com", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' blob:;", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthEndpointReportsHealthy()
    {
        using var client = CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health", Cancellation));
    }

    [Fact]
    public async Task UnknownPathsGetTheNotFoundPage()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/no-such-page", Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Nothing here", await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    [InlineData(ChaosFactory.AllowedOrigin, HttpStatusCode.OK)]
    public async Task HubNegotiationChecksTheOrigin(string origin, HttpStatusCode expected)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task HubRefusesTransportsOtherThanWebSockets()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/_blazor?id=abc", Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PagesAreCompressedWhenAsked()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/about");
        request.Headers.AcceptEncoding.ParseAdd("br");
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task FormPostsAreRejected()
    {
        using var client = CreateClient();
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("text", "hi")]);
        using var response = await client.PostAsync("/board", content, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/app.css")]
    [InlineData("/theme-init.js")]
    [InlineData("/favicon.svg")]
    public async Task StaticAssetsAreServed(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
}
