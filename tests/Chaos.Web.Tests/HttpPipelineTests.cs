using System.Net;
using System.Text.Json;
using Chaos.Web.Infrastructure;
using Chaos.Web.Latency;
using Chaos.Web.Messages;
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
    public async Task ResponsesCarrySecurityHeaders()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/", Cancellation);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(SecurityHeaders.PermissionsPolicy, response.Headers.GetValues("Permissions-Policy").Single());
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
    public async Task ThePolicyAllowsTheLatencyEndpoints()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/", Cancellation);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.StartsWith("default-src 'self'; script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("https://dynamodb.us-east-1.amazonaws.com", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
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
