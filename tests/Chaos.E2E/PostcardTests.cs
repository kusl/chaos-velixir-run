using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed partial class PostcardTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAADCAIAAAA7ljmRAAAADnRFWHRDb21tZW50AHNlY3JldGSqxSkAAAAQSURBVHicY7hgYABHDDg5AB2WDkENblrqAAAAAElFTkSuQmCC");

    private const string StoredImages = """
        id => new Promise((resolve, reject) => {
            const request = indexedDB.open('chaos-postcards');
            request.onsuccess = () => {
                const db = request.result;
                const all = db.transaction('images', 'readonly').objectStore('images').getAll(IDBKeyRange.bound(`${id}/`, `${id}/￿`));
                all.onsuccess = () => { db.close(); resolve(all.result.length); };
                all.onerror = () => reject(all.error);
            };
            request.onerror = () => reject(request.error);
        })
        """;

    [Fact]
    public async Task PostcardsReachOtherBrowsersWithTheirImages()
    {
        await using var first = await browser.NewContextAsync();
        await using var second = await browser.NewContextAsync();
        var writer = await OpenAsync(first);
        var reader = await OpenAsync(second);
        var text = $"postcard {Guid.NewGuid():N}\n\nwith a second paragraph";

        var id = await SendAsync(writer, text, Png);

        var mine = writer.Locator($"#postcard-feed .card[data-id='{id}']");
        await Expect(mine.Locator(".yours")).ToHaveTextAsync("Your message");
        var theirs = reader.Locator($"#postcard-feed .card[data-id='{id}']");
        await Expect(theirs.Locator(".text")).ToHaveTextAsync(text);
        await Expect(theirs.Locator(".yours")).ToHaveCountAsync(0);
        await Expect(theirs).Not.ToHaveClassAsync(Archived());
        await Expect(theirs.Locator(".images img")).ToHaveJSPropertyAsync("naturalWidth", 4);
        await Expect(theirs.Locator(".images img")).ToHaveAttributeAsync("src", BlobUrl());
        await EventuallyAsync(async () => await reader.EvaluateAsync<int>(StoredImages, id) == 1);
    }

    [Fact]
    public async Task TheServerKeepsOnlyTheNewestPostcardButBrowsersKeepTheRest()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        var older = await SendAsync(page, $"older {Guid.NewGuid():N}", Png);
        await EventuallyAsync(async () => await page.EvaluateAsync<int>(StoredImages, older) == 1);
        var newer = await SendAsync(page, $"newer {Guid.NewGuid():N}");

        var old = page.Locator($"#postcard-feed .card[data-id='{older}']");
        await Expect(old).ToHaveClassAsync(Archived());
        await Expect(page.Locator($"#postcard-feed .card[data-id='{newer}']")).Not.ToHaveClassAsync(Archived());
        var gone = await page.APIRequest.GetAsync($"/postcard/images/{older}/0");
        Assert.Equal(404, gone.Status);

        await page.ReloadAsync();
        await Expect(page.Locator("#postcard-text")).ToBeEnabledAsync();
        await Expect(old).ToHaveClassAsync(Archived());
        await Expect(old.Locator(".yours")).ToHaveTextAsync("Your message");
        await Expect(old.Locator(".images img")).ToHaveJSPropertyAsync("naturalWidth", 4);
    }

    [Fact]
    public async Task ImagesAreCleanedOnTheServer()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        var id = await SendAsync(page, "", Png);

        var image = await page.APIRequest.GetAsync($"/postcard/images/{id}/0");
        Assert.Equal(200, image.Status);
        Assert.Equal("image/png", image.Headers["content-type"]);
        var body = await image.BodyAsync();
        Assert.True(body.Length < Png.Length);
        Assert.DoesNotContain("secret", System.Text.Encoding.ASCII.GetString(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheComposerCountsAndOnlyTakesImages()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        var count = page.Locator("#postcard-count");
        await Expect(count).ToHaveTextAsync("0 / 5000");

        await page.Locator("#postcard-text").PressSequentiallyAsync("abc");
        await Expect(count).ToHaveTextAsync("3 / 5000");

        await page.Locator("#postcard-files").SetInputFilesAsync(new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = "hello"u8.ToArray() });
        await Expect(page.Locator("#postcard-status")).ToHaveTextAsync("Only PNG, JPEG and GIF images can be attached.");
        await Expect(page.Locator("#postcard-picked li")).ToHaveCountAsync(0);

        await page.Locator("#postcard-files").SetInputFilesAsync(new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = Png });
        await Expect(page.Locator("#postcard-picked li")).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove image 1" }).ClickAsync();
        await Expect(page.Locator("#postcard-picked li")).ToHaveCountAsync(0);

        await page.Locator("#postcard-text").FillAsync($"counted {Guid.NewGuid():N}");
        await page.Locator("#postcard-text").PressAsync("Control+Enter");
        await Expect(page.Locator("#postcard-text")).ToHaveValueAsync("");
        await Expect(count).ToHaveTextAsync("0 / 5000");
    }

    [Fact]
    public async Task ClearingLocalHistoryDeletesThePostcards()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        await SendAsync(page, $"to be cleared {Guid.NewGuid():N}");

        await page.GotoAsync("/about");
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear local history" }).ClickAsync();
        await Expect(page.Locator("#clear-status")).ToHaveTextAsync("Local history cleared.");

        var names = await page.EvaluateAsync<string[]>("async () => (await indexedDB.databases()).map(d => d.name)");
        Assert.DoesNotContain("chaos-postcards", names);
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/postcard");
        await Expect(page.Locator("#postcard-text")).ToBeEnabledAsync();
        return page;
    }

    private static async Task<string> SendAsync(IPage page, string text, byte[]? image = null)
    {
        await page.Locator("#postcard-text").FillAsync(text);
        if (image is not null)
        {
            await page.Locator("#postcard-files").SetInputFilesAsync(new FilePayload { Name = "dot.png", MimeType = "image/png", Buffer = image });
            await Expect(page.Locator("#postcard-picked li")).ToHaveCountAsync(1);
        }

        var before = await page.Locator("#postcard-feed .card.mine").CountAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(page.Locator("#postcard-text")).ToHaveValueAsync("");
        await Expect(page.Locator("#postcard-feed .card.mine")).ToHaveCountAsync(before + 1);
        var card = page.Locator("#postcard-feed .card").First;
        await Expect(card).ToHaveClassAsync(Mine());
        return await card.GetAttributeAsync("data-id") ?? throw new InvalidOperationException("The postcard has no id.");
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

    [GeneratedRegex(@"\bmine\b")]
    private static partial Regex Mine();

    [GeneratedRegex("^blob:")]
    private static partial Regex BlobUrl();
}
