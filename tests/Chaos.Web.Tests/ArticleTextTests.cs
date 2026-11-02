using System.Text;
using Chaos.Web.Reading;

namespace Chaos.Web.Tests;

public sealed class ArticleTextTests
{
    [Fact]
    public void ParsesTheHeadSummaryAndBody()
    {
        var article = ArticleText.Parse(ArticleSamples.First);

        Assert.NotNull(article);
        Assert.Equal(ArticleText.IdOf(ArticleSamples.FirstUrl), article.Id);
        Assert.Equal("Are Babies Conscious?", article.Title);
        Assert.Equal("By pinpointing when it begins in young humans, scientists hope to better understand what consciousness is.", article.Dek);
        Assert.Equal("By Carl Zimmer", article.Byline);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero), article.Published);
        Assert.Equal(new Uri(ArticleSamples.FirstUrl), article.Source);
        Assert.Equal("When does it begin?", article.Summary);
        Assert.Equal(4, article.Blocks.Count);
        Assert.Equal("A newborn’s brain is busy.", article.Blocks[0].Text);
        Assert.False(article.Blocks[0].IsList);
        Assert.True(article.Blocks[2].IsList);
        Assert.Equal(new[] { "Attention to faces.", "Surprise at the unexpected.", "A sense of time." }, article.Blocks[2].Lines);
    }

    [Fact]
    public void ReadsAHeadWithoutADek()
    {
        var article = ArticleText.Parse(ArticleSamples.Record("x.html", "s", "Title\nBy Someone\nPublished 2026-10-05 13:10 UTC\nhttps://example.com/live", "Body."));

        Assert.NotNull(article);
        Assert.Equal("Title", article.Title);
        Assert.Null(article.Dek);
        Assert.Equal("By Someone", article.Byline);
        Assert.Equal(ArticleText.IdOf("https://example.com/live"), article.Id);
    }

    [Fact]
    public void ToleratesAnUnreadableDateAndNoSource()
    {
        var article = ArticleText.Parse(ArticleSamples.Record("x.html", "s", "Title\nPublished sometime", "Body."));

        Assert.NotNull(article);
        Assert.Null(article.Published);
        Assert.Null(article.Source);
        Assert.Null(article.Byline);
        Assert.Null(article.Dek);
        Assert.Equal(ArticleText.IdOf("Title\nBody."), article.Id);
    }

    [Fact]
    public void KeepsLineBreaksInsideAParagraphThatIsNotAList()
    {
        var article = ArticleText.Parse(ArticleSamples.Record("x.html", "s", "Title", "First line\n- second line"));

        Assert.NotNull(article);
        Assert.False(article.Blocks[0].IsList);
        Assert.Equal("First line\n- second line", article.Blocks[0].Text);
    }

    [Fact]
    public void FallsBackToPlainText()
    {
        var article = ArticleText.Parse("﻿A plain title\r\n\r\nFirst paragraph.\r\n\r\nSecond paragraph.\r\n");

        Assert.NotNull(article);
        Assert.Equal("A plain title", article.Title);
        Assert.Null(article.Summary);
        Assert.Equal(new[] { "First paragraph.", "Second paragraph." }, article.Blocks.Select(block => block.Text));
    }

    [Fact]
    public void NamesAnUntitledRecordAfterItsSlug()
    {
        var article = ArticleText.Parse($"{ArticleSamples.Rule}\nlong-walks-home.html\n{ArticleSamples.Rule}\n\n{new string('w', 300)}\n");

        Assert.NotNull(article);
        Assert.Equal("Long walks home", article.Title);
        Assert.Single(article.Blocks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("====================\nslug\n====================\n\n")]
    public void RejectsRecordsWithoutABody(string text) => Assert.Null(ArticleText.Parse(text));

    [Fact]
    public void SplitsRecordsAtHeaderRules()
    {
        var bytes = Encoding.UTF8.GetBytes(ArticleSamples.Both);
        var records = ArticleText.Records(bytes);

        Assert.Equal(2, records.Count);
        var titles = records.Select(range => ArticleText.Parse(Encoding.UTF8.GetString(bytes[range]))!.Title);
        Assert.Equal(new[] { "Are Babies Conscious?", "The 7 Keys to Longevity" }, titles);
        Assert.Equal(bytes.Length, records[^1].End.GetOffset(bytes.Length));
    }

    [Fact]
    public void IgnoresARuleInsideABody()
    {
        var text = ArticleSamples.Record("a.html", "s", "Alpha", "One.", ArticleSamples.Rule, "Two.", "Three.", "Four.", "Five.", "Six.", "Seven.", "Eight.") + ArticleSamples.Second;
        var bytes = Encoding.UTF8.GetBytes("﻿" + text);
        var records = ArticleText.Records(bytes);

        Assert.Equal(2, records.Count);
        Assert.Equal("Alpha", ArticleText.Parse(Encoding.UTF8.GetString(bytes[records[0]]))!.Title);
    }

    [Fact]
    public void TreatsAFileWithoutRulesAsOneRecord()
    {
        Assert.Single(ArticleText.Records("Just some text.\n"u8));
        Assert.Empty(ArticleText.Records(" \r\n\t\n"u8));
        Assert.Empty(ArticleText.Records([]));
    }

    [Theory]
    [InlineData("0123456789ab", true)]
    [InlineData("0123456789AB", false)]
    [InlineData("0123456789a", false)]
    [InlineData("0123456789abc", false)]
    [InlineData("../../secret", false)]
    [InlineData(null, false)]
    public void ValidatesIds(string? id, bool valid) => Assert.Equal(valid, ArticleText.IsId(id));

    [Fact]
    public void MakesShortStableIds()
    {
        var id = ArticleText.IdOf(ArticleSamples.FirstUrl);
        Assert.Equal(ArticleText.IdLength, id.Length);
        Assert.True(ArticleText.IsId(id));
        Assert.Equal(id, ArticleText.IdOf(ArticleSamples.FirstUrl));
        Assert.NotEqual(id, ArticleText.IdOf(ArticleSamples.SecondUrl));
    }
}
