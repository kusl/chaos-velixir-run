using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class MessageTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("\u200B\u200B")]
    public void BlankInputBecomesEmpty(string? input)
    {
        Assert.Equal("", MessageText.Normalize(input));
    }

    [Fact]
    public void StripsControlCharactersButKeepsNewlines()
    {
        Assert.Equal("a\nb", MessageText.Normalize("a\u0007\r\nb\u0000"));
    }

    [Fact]
    public void CollapsesRunsOfBlankLines()
    {
        Assert.Equal("a\n\nb", MessageText.Normalize("a\n\n\n\n\nb"));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("hi", MessageText.Normalize("  hi \n"));
    }

    [Fact]
    public void KeepsTextAtTheLimit()
    {
        var input = new string('x', MessageText.MaxLength);
        Assert.Equal(input, MessageText.Normalize(input));
    }

    [Fact]
    public void TruncatesOnGraphemeBoundaries()
    {
        var prefix = new string('a', MessageText.MaxLength - 3);
        Assert.Equal(prefix, MessageText.Normalize(prefix + "👨‍👩‍👧‍👦"));
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData(" hello", false)]
    [InlineData("a\n\n\nb", false)]
    [InlineData("", false)]
    public void RecognisesNormalizedText(string text, bool expected)
    {
        Assert.Equal(expected, MessageText.IsNormalized(text));
    }

    [Fact]
    public void TakesALongerLimitWhenAsked()
    {
        var input = new string('x', 2000);
        Assert.Equal(input, MessageText.Normalize(input, 5000));
        Assert.True(MessageText.IsNormalized(input, 5000));
        Assert.False(MessageText.IsNormalized(input));
        Assert.Equal(MessageText.MaxLength, MessageText.Normalize(input).Length);
    }
}
