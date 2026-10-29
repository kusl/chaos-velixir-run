using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed class LatencyTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task ATestReachesEveryoneAndUnlocksAutoRun()
    {
        await using var firstContext = await OfflineContextAsync();
        await using var secondContext = await OfflineContextAsync();
        var tester = await firstContext.NewPageAsync();
        await tester.GotoAsync("/about");
        await tester.EvaluateAsync("() => localStorage.setItem('chaos-latency-runs', '9')");
        await OpenAsync(tester);
        var watcher = await secondContext.NewPageAsync();
        await OpenAsync(watcher);

        var auto = tester.GetByRole(AriaRole.Button, new() { Name = "Auto run" });
        await Expect(auto).ToBeHiddenAsync();
        await tester.GetByRole(AriaRole.Button, new() { Name = "Run test" }).ClickAsync();

        var mine = tester.Locator("#latency-feed .msg.mine").First;
        await Expect(mine).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(auto).ToBeVisibleAsync();
        await Expect(tester.Locator(".probe .status")).ToHaveTextAsync("Done. Your result is at the top of the list.");
        await Expect(tester.Locator(".probe tr[data-key='chaos'] .browser")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^\\d+ ms$"));

        var id = await mine.GetAttributeAsync("data-id") ?? throw new InvalidOperationException("The result has no id.");
        var seen = watcher.Locator($"#latency-feed .msg[data-id='{id}']");
        await Expect(seen).ToBeVisibleAsync();
        await Expect(seen.Locator(".yours")).ToHaveCountAsync(0);
        await seen.Locator("summary").ClickAsync();
        await Expect(seen.Locator("tbody tr")).ToHaveCountAsync(await tester.Locator(".probe tbody tr").CountAsync());
        await Expect(watcher.Locator("#latency-browser-fastest-empty")).ToBeVisibleAsync();
        await Expect(watcher.Locator("#latency-jackpot-winners-empty")).ToBeVisibleAsync();
    }

    private async Task<IBrowserContext> OfflineContextAsync()
    {
        var context = await browser.NewContextAsync();
        await context.RouteAsync(url => !url.StartsWith(BrowserFixture.BaseUrl, StringComparison.Ordinal), route => route.AbortAsync());
        return context;
    }

    private static async Task OpenAsync(IPage page)
    {
        await page.GotoAsync("/latency");
        await Expect(page.Locator(".probe[data-ready]")).ToBeVisibleAsync();
    }
}
