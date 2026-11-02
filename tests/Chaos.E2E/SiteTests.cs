using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed partial class SiteTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task PagesLoadWithoutErrorsOrCookies()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Add(message.Text);
            }
        };
        page.PageError += (_, error) => errors.Add(error);

        foreach (var path in new[] { "/", "/about", "/board", "/postcard", "/slots", "/latency", "/read", "/canvas" })
        {
            await page.GotoAsync(path);
        }

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reset canvas" })).ToBeEnabledAsync();
        Assert.Empty(errors);
        Assert.Empty(await context.CookiesAsync());
    }

    [Fact]
    public async Task OnlyTheInteractivePagesOpenAWebSocket()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var sockets = new List<string>();
        page.WebSocket += (_, socket) => sockets.Add(socket.Url);

        await page.GotoAsync("/about");
        await page.GotoAsync("/");
        await page.WaitForTimeoutAsync(1000);
        Assert.Empty(sockets);

        await page.GotoAsync("/board");
        await Expect(page.Locator("#compose-text")).ToBeEnabledAsync();
        Assert.NotEmpty(sockets);
    }

    [Theory]
    [InlineData(320, 640)]
    [InlineData(390, 844)]
    public async Task NoPageScrollsSidewaysOnAPhone(int width, int height)
    {
        await using var context = await browser.NewSizedContextAsync(width, height);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/board");
        await Expect(page.Locator("#compose-text")).ToBeEnabledAsync();
        var text = $"{Guid.NewGuid():N}{new string('w', 400)}";
        await page.Locator("#compose-text").FillAsync(text);
        await page.Locator("#compose-text").PressAsync("Control+Enter");
        await Expect(page.Locator("#board-feed .msg", new() { HasText = text })).ToBeVisibleAsync();

        foreach (var path in new[] { "/", "/board", "/postcard", "/canvas", "/slots", "/latency", "/read", "/about", "/no-such-page" })
        {
            await page.GotoAsync(path);
            var nav = page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });
            foreach (var name in new[] { "Board", "Postcard", "Canvas", "Slots", "Latency", "Read", "About" })
            {
                await Expect(nav.GetByRole(AriaRole.Link, new() { Name = name, Exact = true })).ToBeInViewportAsync();
            }

            var widths = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
            Assert.True(widths[0] <= widths[1], $"{path} is {widths[0]} px wide in a {widths[1]} px viewport.");
        }
    }

    [Fact]
    public async Task ThemeChoiceSurvivesReloadAndNavigation()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var root = page.Locator("html");

        await page.GotoAsync("/about");
        await page.GetByRole(AriaRole.Button, new() { Name = "Dark theme" }).ClickAsync();
        await Expect(root).ToHaveAttributeAsync("data-theme", "dark");

        await page.ReloadAsync();
        await Expect(root).ToHaveAttributeAsync("data-theme", "dark");

        await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Canvas" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(CanvasUrl());
        await Expect(root).ToHaveAttributeAsync("data-theme", "dark");
    }

    [Fact]
    public async Task ResponsesCarryAStrictPolicyAndACustomNotFoundPage()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var home = await page.GotoAsync("/") ?? throw new InvalidOperationException("No response.");
        Assert.StartsWith("default-src 'self'; script-src 'self'", home.Headers["content-security-policy"], StringComparison.Ordinal);

        var missing = await page.GotoAsync("/no-such-page") ?? throw new InvalidOperationException("No response.");
        Assert.Equal(404, missing.Status);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Nothing here" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheFooterClockTicksWithTheServerTime()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        var clock = page.Locator("#server-clock");
        await Expect(clock).ToHaveTextAsync(ClockFormat());
        var first = await clock.TextContentAsync();
        await Expect(clock).Not.ToHaveTextAsync(first ?? "", new() { Timeout = 5000 });

        await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Board" }).ClickAsync();
        await Expect(clock).ToHaveTextAsync(ClockFormat());
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC([+-]\d{2}:\d{2})?$")]
    private static partial Regex ClockFormat();

    [GeneratedRegex("/canvas$")]
    private static partial Regex CanvasUrl();
}
