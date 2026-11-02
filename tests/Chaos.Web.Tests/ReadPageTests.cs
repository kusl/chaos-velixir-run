using System.Net;
using System.Text;
using Chaos.Web.Reading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chaos.Web.Tests;

public sealed class ReadPageTests : IClassFixture<ChaosFactory>, IDisposable
{
    private const string FileName = "articles.txt";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("chaos-read-");
    private readonly PhysicalFileProvider _files;
    private readonly WebApplicationFactory<Program> _factory;

    public ReadPageTests(ChaosFactory factory)
    {
        _files = new PhysicalFileProvider(_root.FullName);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new ArticleLibrary(_files, Limits.Create(options => options.ArticlesFile = FileName), NullLogger<ArticleLibrary>.Instance))));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _factory.Dispose();
        _files.Dispose();
        _root.Delete(true);
    }

    [Fact]
    public async Task ARandomVisitRedirectsToAnArticle()
    {
        Write(ArticleSamples.Both);
        using var client = CreateClient();
        using var response = await client.GetAsync("/read", Cancellation);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var target = response.Headers.Location?.ToString() ?? "";
        Assert.Contains(target[(target.LastIndexOf('/') + 1)..], new[] { ArticleText.IdOf(ArticleSamples.FirstUrl), ArticleText.IdOf(ArticleSamples.SecondUrl) });
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task TheRedirectStaysOnTheHostTheBrowserUsed()
    {
        Write(ArticleSamples.First);
        using var client = CreateClient();
        foreach (var path in new[] { "/read", "/read/" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Host = "chaos-eu.velixir.run";
            request.Headers.Add("blazor-enhanced-nav", "on");
            using var response = await client.SendAsync(request, Cancellation);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal($"/read/{ArticleText.IdOf(ArticleSamples.FirstUrl)}", response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task AnArticleRendersAsPlainReadableText()
    {
        Write(ArticleSamples.Both);
        var id = ArticleText.IdOf(ArticleSamples.FirstUrl);
        using var client = CreateClient();
        using var response = await client.GetAsync($"/read/{id}", Cancellation);
        var html = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains("<title>Are Babies Conscious? · Chaos</title>", html, StringComparison.Ordinal);
        Assert.Contains($"data-reading=\"{id}\"", html, StringComparison.Ordinal);
        Assert.Matches("<h1[^>]*>Are Babies Conscious\\?</h1>", html);
        Assert.Contains("By Carl Zimmer", html, StringComparison.Ordinal);
        Assert.Matches("<time[^>]*datetime=\"2026-10-05T09:00Z\"[^>]*>October 5, 2026</time>", html);
        Assert.Matches("<li[^>]*>Surprise at the unexpected.</li>", html);
        Assert.Contains("id=\"reading-history\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{ArticleSamples.FirstUrl}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingArticleSaysSoInsteadOfFailing()
    {
        Write(ArticleSamples.Second);
        using var client = CreateClient();
        var html = await client.GetStringAsync($"/read/{ArticleText.IdOf(ArticleSamples.FirstUrl)}", Cancellation);

        Assert.Matches("<h1[^>]*>Not available</h1>", html);
        Assert.Contains("id=\"reading-history\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoFileMeansNothingToReadRatherThanAnError()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/read", Cancellation);
        var html = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("nothing to read right now", html, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private void Write(string text) =>
        File.WriteAllText(Path.Combine(_root.FullName, FileName), text, new UTF8Encoding(false));
}
