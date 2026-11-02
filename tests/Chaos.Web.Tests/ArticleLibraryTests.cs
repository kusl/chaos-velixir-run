using System.Text;
using Chaos.Web.Infrastructure;
using Chaos.Web.Reading;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Chaos.Web.Tests;

public sealed class ArticleLibraryTests : IDisposable
{
    private const string FileName = "articles.txt";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("chaos-articles-");
    private readonly PhysicalFileProvider _files;

    public ArticleLibraryTests() => _files = new PhysicalFileProvider(_root.FullName);

    private string FilePath => Path.Combine(_root.FullName, FileName);

    public void Dispose()
    {
        _files.Dispose();
        _root.Delete(true);
    }

    [Fact]
    public void FindsEveryArticleById()
    {
        Write(ArticleSamples.Both);
        var library = Create();

        Assert.Equal(2, library.Count);
        Assert.Equal("Are Babies Conscious?", library.Find(ArticleText.IdOf(ArticleSamples.FirstUrl))?.Title);
        Assert.Equal("The 7 Keys to Longevity", library.Find(ArticleText.IdOf(ArticleSamples.SecondUrl))?.Title);
    }

    [Fact]
    public void PicksAnyArticle()
    {
        Write(ArticleSamples.Both);
        var library = Create();

        Assert.Equal("Are Babies Conscious?", library.Pick(_ => 0)?.Title);
        Assert.Equal("The 7 Keys to Longevity", library.Pick(count => count - 1)?.Title);
        Assert.Null(library.Pick(count => count));
        var any = library.Random();
        Assert.NotNull(any);
        Assert.Contains(any.Title, new[] { "Are Babies Conscious?", "The 7 Keys to Longevity" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ffffffffffff")]
    [InlineData("FFFFFFFFFFFF")]
    [InlineData("../articles.txt")]
    public void ReturnsNothingForUnknownIds(string? id)
    {
        Write(ArticleSamples.Both);
        Assert.Null(Create().Find(id));
    }

    [Fact]
    public void KeepsTheFirstOfDuplicateArticles()
    {
        Write(ArticleSamples.First + ArticleSamples.First.Replace("Are Babies Conscious?", "Copy", StringComparison.Ordinal) + ArticleSamples.Second);
        var library = Create();

        Assert.Equal(2, library.Count);
        Assert.Equal("Are Babies Conscious?", library.Find(ArticleText.IdOf(ArticleSamples.FirstUrl))?.Title);
    }

    [Fact]
    public void IsEmptyWithoutAFile()
    {
        var library = Create();

        Assert.Equal(0, library.Count);
        Assert.Null(library.Random());
        Assert.Null(library.Find(ArticleText.IdOf(ArticleSamples.FirstUrl)));
    }

    [Fact]
    public void IsEmptyWhenThePathIsAFolder()
    {
        Directory.CreateDirectory(FilePath);
        Assert.Equal(0, Create().Count);
    }

    [Fact]
    public void NoticesWhenTheFileAppearsChangesAndDisappears()
    {
        var library = Create();
        Assert.Equal(0, library.Count);

        Write(ArticleSamples.First);
        Assert.Equal(1, library.Count);

        Write(ArticleSamples.Second + ArticleSamples.First, TimeSpan.FromMinutes(1));
        var first = ArticleText.IdOf(ArticleSamples.FirstUrl);
        Assert.Equal(2, library.Count);
        Assert.Equal("Are Babies Conscious?", library.Find(first)?.Title);

        Write(ArticleSamples.Second, TimeSpan.FromMinutes(2));
        Assert.Null(library.Find(first));
        Assert.Equal("The 7 Keys to Longevity", library.Random()?.Title);

        File.Delete(FilePath);
        Assert.Null(library.Random());
        Assert.Equal(0, library.Count);
    }

    [Fact]
    public void ReadsAPlainTextFileAsOneArticle()
    {
        Write("A title\n\nThe only paragraph.\n");
        var article = Create().Random();

        Assert.Equal("A title", article?.Title);
        Assert.Equal("The only paragraph.", Assert.Single(article!.Blocks).Text);
    }

    [Fact]
    public void ReadsArticlesAfterMultibyteText()
    {
        Write("﻿" + ArticleSamples.Record("é.html", "ü", "Ünïcödé — “quotes”\nBy Ñame\nPublished 2026-10-01 00:00 UTC\nhttps://example.com/u", "Body ½ €.") + ArticleSamples.Second);
        var library = Create();

        Assert.Equal("Ünïcödé — “quotes”", library.Find(ArticleText.IdOf("https://example.com/u"))?.Title);
        Assert.Equal("The 7 Keys to Longevity", library.Find(ArticleText.IdOf(ArticleSamples.SecondUrl))?.Title);
    }

    [Fact]
    public void ReadsFromProvidersThatCannotSeek()
    {
        var library = new ArticleLibrary(new MemoryFiles(Encoding.UTF8.GetBytes(ArticleSamples.Both)), Limits.Create(options => options.ArticlesFile = FileName), NullLogger<ArticleLibrary>.Instance);

        Assert.Equal("The 7 Keys to Longevity", library.Find(ArticleText.IdOf(ArticleSamples.SecondUrl))?.Title);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("nytimes.txt", true)]
    public void TheArticlesFileMustBeNamed(string name, bool valid) =>
        Assert.Equal(valid, new ChaosOptions { ArticlesFile = name }.IsValid());

    private ArticleLibrary Create() =>
        new(_files, Limits.Create(options => options.ArticlesFile = FileName), NullLogger<ArticleLibrary>.Instance);

    private void Write(string text, TimeSpan? later = null)
    {
        File.WriteAllText(FilePath, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(FilePath, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) + (later ?? TimeSpan.Zero));
    }

    private sealed class MemoryFiles(byte[] bytes) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath) => new MemoryFile(bytes);

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class MemoryFile(byte[] bytes) : IFileInfo
    {
        public bool Exists => true;

        public long Length => bytes.Length;

        public string? PhysicalPath => null;

        public string Name => FileName;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new ForwardOnlyStream(bytes);
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => false;
    }
}
