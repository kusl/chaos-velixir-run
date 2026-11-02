using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed partial class ReadTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    private const string Settled = """
        ([first, reads]) => {
            const id = document.querySelector('article[data-reading]')?.dataset.reading;
            const top = document.querySelector('#reading-history li');
            return !!top && top.dataset.id === id && (id !== first || top.textContent.includes(reads));
        }
        """;

    [Fact]
    public async Task ArticlesAreRememberedOnThisDeviceAndCanBeReadAgain()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/board");
        await page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Read", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(ArticleUrl());
        var first = await ArticleIdAsync(page);
        var firstUrl = page.Url;
        var title = await page.Locator("article h1").TextContentAsync() ?? "";
        Assert.NotEmpty(title);
        await Expect(History(page)).ToHaveCountAsync(1);
        await Expect(page.Locator("#reading-empty")).ToBeHiddenAsync();
        await Expect(History(page).Locator("a[aria-current='page']")).ToHaveTextAsync(title);

        var second = first;
        var firstReads = 1;
        for (var attempt = 0; attempt < 5 && second == first; attempt++)
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Another one" }).ClickAsync();
            await page.WaitForFunctionAsync(Settled, new[] { first, $"{firstReads + 1} reads" });
            second = await ArticleIdAsync(page);
            if (second == first)
            {
                firstReads++;
            }
        }

        Assert.NotEqual(first, second);
        await Expect(History(page)).ToHaveCountAsync(2);
        await Expect(History(page).First).ToHaveAttributeAsync("data-id", second);

        await page.ReloadAsync();
        await Expect(History(page)).ToHaveCountAsync(2);
        await History(page).Locator($"a[href='read/{first}']").ClickAsync();
        await Expect(page).ToHaveURLAsync(firstUrl);
        await Expect(page.Locator("article h1")).ToHaveTextAsync(title);
        await Expect(History(page).First).ToHaveAttributeAsync("data-id", first);
        await Expect(History(page).First).ToContainTextAsync($"{firstReads + 1} reads");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task AnArticleThatIsGoneSaysSo()
    {
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/read/000000000000");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Not available" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Read a random one" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(ArticleUrl());
        await Expect(History(page)).ToHaveCountAsync(1);
    }

    private static ILocator History(IPage page) => page.Locator("#reading-history li");

    private static async Task<string> ArticleIdAsync(IPage page) =>
        await page.Locator("article[data-reading]").GetAttributeAsync("data-reading") ?? throw new InvalidOperationException("The article has no id.");

    [GeneratedRegex("/read/[0-9a-f]{12}$")]
    private static partial Regex ArticleUrl();
}
