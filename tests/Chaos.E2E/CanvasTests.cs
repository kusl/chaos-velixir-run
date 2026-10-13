using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Chaos.E2E;

public sealed class CanvasTests(BrowserFixture browser) : IClassFixture<BrowserFixture>
{
    private const string Ink = """
        ([x, y]) => {
            const canvas = document.querySelector('canvas.paper');
            const pixel = canvas.getContext('2d').getImageData(Math.floor(x * canvas.width), Math.floor(y * canvas.height), 1, 1).data;
            return pixel[3] > 0;
        }
        """;

    private const string Blank = """
        ([x, y]) => {
            const canvas = document.querySelector('canvas.paper');
            const pixel = canvas.getContext('2d').getImageData(Math.floor(x * canvas.width), Math.floor(y * canvas.height), 1, 1).data;
            return pixel[3] === 0;
        }
        """;

    [Fact]
    public async Task StrokesReachEveryoneAndResetClearsThem()
    {
        await using var firstContext = await browser.NewContextAsync();
        await using var secondContext = await browser.NewContextAsync();
        var artist = await OpenCanvasAsync(firstContext);
        var viewer = await OpenCanvasAsync(secondContext);

        var box = await artist.Locator("canvas.paper").BoundingBoxAsync() ?? throw new InvalidOperationException("The canvas has no size.");
        await artist.Mouse.MoveAsync(box.X + (box.Width * 0.2f), box.Y + (box.Height * 0.2f));
        await artist.Mouse.DownAsync();
        await artist.Mouse.MoveAsync(box.X + (box.Width * 0.8f), box.Y + (box.Height * 0.8f), new() { Steps = 25 });
        await artist.Mouse.UpAsync();

        var centre = new[] { 0.5, 0.5 };
        await viewer.WaitForFunctionAsync(Ink, centre);

        await using var lateContext = await browser.NewContextAsync();
        var late = await OpenCanvasAsync(lateContext);
        await late.WaitForFunctionAsync(Ink, centre);

        await viewer.GetByRole(AriaRole.Button, new() { Name = "Reset canvas" }).ClickAsync();
        await artist.WaitForFunctionAsync(Blank, centre);
        await late.WaitForFunctionAsync(Blank, centre);
    }

    private static async Task<IPage> OpenCanvasAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/canvas");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reset canvas" })).ToBeEnabledAsync();
        await Expect(page.Locator("canvas.paper[data-ready]")).ToBeVisibleAsync();
        return page;
    }
}
