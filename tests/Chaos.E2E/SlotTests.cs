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
    public async Task TenPressesUnlockAutoSpinThatKeepsGoingInBackgroundTabs()
    {
        await using var context = await browser.NewContextAsync();
        await context.AddInitScriptAsync("Object.defineProperty(document, 'visibilityState', { get: () => 'hidden' }); Object.defineProperty(document, 'hidden', { get: () => true });");
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

    [Fact]
    public async Task WinnersAreKeptAndExplainTheirOdds()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        var id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(-1)).ToString();
        await page.EvaluateAsync(
            """
            ([id, text]) => new Promise((resolve, reject) => {
                const request = indexedDB.open('chaos-slots', 1);
                request.onupgradeneeded = () => request.result.createObjectStore('spins', { keyPath: 'id' });
                request.onerror = () => reject(request.error);
                request.onsuccess = () => {
                    const db = request.result;
                    const transaction = db.transaction('spins', 'readwrite');
                    transaction.objectStore('spins').put({ id, text, mine: false });
                    transaction.oncomplete = () => {
                        db.close();
                        resolve();
                    };
                    transaction.onerror = () => reject(transaction.error);
                };
            })
            """,
            new[] { id, "QQQQ7" });
        await page.GotoAsync("/slots");
        await Expect(page.Locator(".machine[data-ready]")).ToBeVisibleAsync();

        var winner = page.Locator($"#slots-winners .msg[data-id='{id}']");
        await Expect(winner).ToBeVisibleAsync();
        await Expect(winner.Locator(".lesson")).ToHaveCountAsync(0);
        await winner.Locator("summary").ClickAsync();
        await Expect(winner.Locator(".lesson")).ToContainTextAsync("1 in 9,598");
        await Expect(page.Locator($"#slots-jackpots .msg.jackpot[data-id='{id}']")).ToBeVisibleAsync();
        await KeepsJackpotAsync(page, id);
    }

    [Fact]
    public async Task JackpotsAreSharedAndNeverForgotten()
    {
        await using var seededContext = await browser.NewContextAsync();
        var page = await seededContext.NewPageAsync();
        await page.GotoAsync("/about");
        var id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(-400)).ToString();
        await page.EvaluateAsync(
            """
            ([id, text]) => new Promise((resolve, reject) => {
                const request = indexedDB.open('chaos-slots', 2);
                request.onupgradeneeded = () => {
                    request.result.createObjectStore('spins', { keyPath: 'id' });
                    request.result.createObjectStore('jackpots', { keyPath: 'id' });
                };
                request.onerror = () => reject(request.error);
                request.onsuccess = () => {
                    const db = request.result;
                    const transaction = db.transaction('jackpots', 'readwrite');
                    transaction.objectStore('jackpots').put({ id, text, mine: true });
                    transaction.oncomplete = () => {
                        db.close();
                        resolve();
                    };
                    transaction.onerror = () => reject(transaction.error);
                };
            })
            """,
            new[] { id, "KKKKK" });
        await page.GotoAsync("/slots");
        await Expect(page.Locator(".machine[data-ready]")).ToBeVisibleAsync();
        var kept = page.Locator($"#slots-jackpots .msg.jackpot[data-id='{id}']");
        await Expect(kept).ToBeVisibleAsync();
        await Expect(kept.Locator(".prize")).ToHaveTextAsync("Jackpot");
        await Expect(kept.Locator(".yours")).ToBeVisibleAsync();

        await using var freshContext = await browser.NewContextAsync();
        var fresh = await OpenSlotsAsync(freshContext);
        var shared = fresh.Locator($"#slots-jackpots .msg.jackpot[data-id='{id}']");
        await Expect(shared).ToBeVisibleAsync();
        await Expect(shared.Locator(".yours")).ToHaveCountAsync(0);
        await Expect(fresh.Locator("#slots-jackpots-empty")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task AJackpotIsCelebratedByEveryone()
    {
        await using var firstContext = await browser.NewContextAsync(BrowserFixture.RiggedUrl);
        await using var secondContext = await browser.NewContextAsync(BrowserFixture.RiggedUrl);
        var presser = await OpenSlotsAsync(firstContext);
        var watcher = await OpenSlotsAsync(secondContext);

        await SpinButton(presser).ClickAsync();
        await Expect(presser.Locator(".machine .status")).ToHaveTextAsync("Your press worked. This spin is yours.");

        foreach (var page in new[] { presser, watcher })
        {
            await Expect(page.Locator(".machine.jackpot")).ToBeVisibleAsync();
            await Expect(page.Locator(".machine .verdict")).ToHaveTextAsync("Jackpot! Five of a kind");
        }

        var mine = presser.Locator("#slots-jackpots .msg.jackpot.mine").First;
        await Expect(mine).ToBeVisibleAsync();
        await Expect(mine.Locator(".symbols")).ToHaveTextAsync("77777");
        var id = await mine.GetAttributeAsync("data-id") ?? throw new InvalidOperationException("The jackpot has no id.");
        await Expect(watcher.Locator($"#slots-jackpots .msg.jackpot[data-id='{id}']")).ToBeVisibleAsync();
        await KeepsJackpotAsync(watcher, id);
        await Expect(presser.Locator(".machine.jackpot")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    private static async Task KeepsJackpotAsync(IPage page, string id)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if ((await KeptJackpotsAsync(page)).Contains(id, StringComparer.Ordinal))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Contains(id, await KeptJackpotsAsync(page));
    }

    private static Task<string[]> KeptJackpotsAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            """
            () => new Promise((resolve, reject) => {
                const request = indexedDB.open('chaos-slots');
                request.onerror = () => reject(request.error);
                request.onsuccess = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains('jackpots')) {
                        db.close();
                        resolve([]);
                        return;
                    }
                    const keys = db.transaction('jackpots', 'readonly').objectStore('jackpots').getAllKeys();
                    keys.onsuccess = () => {
                        db.close();
                        resolve(keys.result);
                    };
                    keys.onerror = () => reject(keys.error);
                };
            })
            """);

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
