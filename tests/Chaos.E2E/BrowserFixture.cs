using Microsoft.Playwright;


namespace Chaos.E2E;

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public static string BaseUrl { get; } = Environment.GetEnvironmentVariable("CHAOS_BASE_URL") ?? "http://127.0.0.1:5080/";

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    public Task<IBrowserContext> NewContextAsync(ColorScheme scheme = ColorScheme.Light)
    {
        var browser = _browser ?? throw new InvalidOperationException("The browser is not running.");
        return browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl, ColorScheme = scheme });
    }
}
