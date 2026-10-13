using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed partial class BoardTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    private const string OldId = "016f5e66-e800-7000-8000-000000000001";

    private const string StoredTexts = """
        () => new Promise((resolve, reject) => {
            const request = indexedDB.open('chaos-board');
            request.onsuccess = () => {
                const db = request.result;
                const all = db.transaction('messages', 'readonly').objectStore('messages').getAll();
                all.onsuccess = () => { db.close(); resolve(all.result.map(m => m.text)); };
                all.onerror = () => reject(all.error);
            };
            request.onerror = () => reject(request.error);
        })
        """;

    private const string SeedMessage = """
        ([id, text]) => new Promise((resolve, reject) => {
            const request = indexedDB.open('chaos-board', 1);
            request.onupgradeneeded = () => request.result.createObjectStore('messages', { keyPath: 'id' });
            request.onsuccess = () => {
                const db = request.result;
                const transaction = db.transaction('messages', 'readwrite');
                transaction.objectStore('messages').put({ id, text });
                transaction.oncomplete = () => { db.close(); resolve(); };
                transaction.onerror = () => reject(transaction.error);
            };
            request.onerror = () => reject(request.error);
        })
        """;

    [Fact]
    public async Task MessagesReachOtherBrowsersAndTheirLocalHistory()
    {
        await using var first = await browser.NewContextAsync();
        await using var second = await browser.NewContextAsync();
        var writer = await OpenBoardAsync(first);
        var reader = await OpenBoardAsync(second);
        var text = $"hello {Guid.NewGuid():N}";

        await PostAsync(writer, text);

        var item = reader.Locator("#board-feed .msg", new() { HasText = text });
        await Expect(item).ToBeVisibleAsync();
        await Expect(item).Not.ToHaveClassAsync(Archived());
        await EventuallyAsync(async () => (await reader.EvaluateAsync<string[]>(StoredTexts)).Contains(text));
    }

    [Fact]
    public async Task ControlEnterSends()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenBoardAsync(context);
        var text = $"keyboard {Guid.NewGuid():N}";

        await page.Locator("#compose-text").FillAsync(text);
        await page.Locator("#compose-text").PressAsync("Control+Enter");

        await Expect(page.Locator("#board-feed .msg", new() { HasText = text })).ToBeVisibleAsync();
        await Expect(page.Locator("#compose-text")).ToHaveValueAsync("");
    }

    [Fact]
    public async Task MessagesTheServerForgotHaveADottedBorder()
    {
        await using var seededContext = await browser.NewContextAsync();
        var seeded = await seededContext.NewPageAsync();
        await seeded.GotoAsync("/about");
        await seeded.EvaluateAsync(SeedMessage, new[] { OldId, "posted in 2020" });

        await using var otherContext = await browser.NewContextAsync();
        var poster = await OpenBoardAsync(otherContext);
        for (var i = 0; i < 6; i++)
        {
            await PostAsync(poster, $"filler {i} {Guid.NewGuid():N}");
        }

        await seeded.GotoAsync("/board");
        var old = seeded.Locator($"#board-feed .msg[data-id='{OldId}']");
        await Expect(old).ToBeVisibleAsync();
        await Expect(old).ToHaveClassAsync(Archived());
    }

    [Fact]
    public async Task ClearingLocalHistoryDeletesTheDatabase()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenBoardAsync(context);
        await PostAsync(page, $"to be cleared {Guid.NewGuid():N}");

        await page.GotoAsync("/about");
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear local message history" }).ClickAsync();
        await Expect(page.Locator("#clear-status")).ToHaveTextAsync("Local message history cleared.");

        var names = await page.EvaluateAsync<string[]>("async () => (await indexedDB.databases()).map(d => d.name)");
        Assert.DoesNotContain("chaos-board", names);
    }

    private static async Task<IPage> OpenBoardAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/board");
        await Expect(page.Locator("#compose-text")).ToBeEnabledAsync();
        return page;
    }

    private static async Task PostAsync(IPage page, string text)
    {
        await page.Locator("#compose-text").FillAsync(text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(page.Locator("#board-feed .msg", new() { HasText = text })).ToBeVisibleAsync();
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(await condition());
    }

    [GeneratedRegex("archived")]
    private static partial Regex Archived();
}
