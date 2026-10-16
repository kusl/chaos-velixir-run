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

    [Fact]
    public async Task FinishedStrokesAreKeptInTheBrowser()
    {
        await using var context = await browser.NewContextAsync();
        var artist = await OpenCanvasAsync(context);
        var box = await artist.Locator("canvas.paper").BoundingBoxAsync() ?? throw new InvalidOperationException("The canvas has no size.");
        await artist.Mouse.MoveAsync(box.X + (box.Width * 0.3f), box.Y + (box.Height * 0.6f));
        await artist.Mouse.DownAsync();
        await artist.Mouse.MoveAsync(box.X + (box.Width * 0.7f), box.Y + (box.Height * 0.6f), new() { Steps = 10 });
        await artist.Mouse.UpAsync();

        await artist.WaitForFunctionAsync(StoredStrokes, 1);

        await artist.GetByRole(AriaRole.Button, new() { Name = "Reset canvas" }).ClickAsync();
        await artist.WaitForFunctionAsync(StoredStrokesAtMost, 0);
    }

    private const string StoredStrokes = """
        async minimum => (await indexedDB.databases()).some(d => d.name === 'chaos-canvas') && await new Promise(resolve => {
            const request = indexedDB.open('chaos-canvas');
            request.onsuccess = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains('strokes')) { db.close(); resolve(false); return; }
                const count = db.transaction('strokes', 'readonly').objectStore('strokes').count();
                count.onsuccess = () => { db.close(); resolve(count.result >= minimum); };
                count.onerror = () => { db.close(); resolve(false); };
            };
            request.onerror = () => resolve(false);
        })
        """;

    private const string StoredStrokesAtMost = """
        async maximum => (await indexedDB.databases()).some(d => d.name === 'chaos-canvas') && await new Promise(resolve => {
            const request = indexedDB.open('chaos-canvas');
            request.onsuccess = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains('strokes')) { db.close(); resolve(false); return; }
                const count = db.transaction('strokes', 'readonly').objectStore('strokes').count();
                count.onsuccess = () => { db.close(); resolve(count.result <= maximum); };
                count.onerror = () => { db.close(); resolve(false); };
            };
            request.onerror = () => resolve(false);
        })
        """;

    private static async Task<IPage> OpenCanvasAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/canvas");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reset canvas" })).ToBeEnabledAsync();
        await Expect(page.Locator("canvas.paper[data-ready]")).ToBeVisibleAsync();
        return page;
    }
}
