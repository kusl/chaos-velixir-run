using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed class SlotTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task APressSpinsForEveryoneAndLandsInTheirResults()
    {
        await using var firstContext = await browser.NewContextAsync();
        await using var secondContext = await browser.NewContextAsync();
        var presser = await OpenSlotsAsync(firstContext);
        var watcher = await OpenSlotsAsync(secondContext);

        await SpinButton(presser).ClickAsync();
        await Expect(presser.Locator(".machine .status")).ToHaveTextAsync("Your press worked. This spin is yours.");

        var mine = presser.Locator("#slots-feed .msg.mine").First;
        await Expect(mine).ToBeVisibleAsync();
        var id = await mine.GetAttributeAsync("data-id") ?? throw new InvalidOperationException("The result has no id.");

        var seen = watcher.Locator($"#slots-feed .msg[data-id='{id}']");
        await Expect(seen).ToBeVisibleAsync();
        await Expect(seen.Locator(".yours")).ToHaveCountAsync(0);
        await Expect(SpinButton(watcher)).ToBeEnabledAsync();
    }

    [Fact]
    public async Task TenPressesUnlockAutoSpin()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        await page.EvaluateAsync("() => localStorage.setItem('chaos-slots-presses', '9')");
        await page.GotoAsync("/slots");
        await Expect(page.Locator(".machine[data-ready]")).ToBeVisibleAsync();

        var auto = page.GetByRole(AriaRole.Button, new() { Name = "Auto spin" });
        await Expect(auto).ToBeHiddenAsync();
        await SpinButton(page).ClickAsync();
        await Expect(auto).ToBeVisibleAsync();

        await auto.ClickAsync();
        await Expect(auto).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("#slots-feed .msg.mine").Nth(2)).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await auto.ClickAsync();
        await Expect(auto).ToHaveAttributeAsync("aria-pressed", "false");
    }

    private static ILocator SpinButton(IPage page) =>
        page.GetByRole(AriaRole.Button, new() { Name = "Spin", Exact = true });

    private static async Task<IPage> OpenSlotsAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/slots");
        await Expect(page.Locator(".machine[data-ready]")).ToBeVisibleAsync();
        return page;
    }
}
