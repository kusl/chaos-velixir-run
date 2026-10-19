using Chaos.Web.Slots;

namespace Chaos.Web.Tests;

public sealed class SlotRulesTests
{
    [Theory]
    [InlineData("7Q7A7", nameof(SlotWin.ThreeOfAKind))]
    [InlineData("XXXXB", nameof(SlotWin.FourOfAKind))]
    [InlineData("KKKKK", nameof(SlotWin.FiveOfAKind))]
    [InlineData("13AKZ", nameof(SlotWin.StraightUp))]
    [InlineData("ZKA80", nameof(SlotWin.StraightDown))]
    [InlineData("ABCDF", nameof(SlotWin.StraightUp))]
    [InlineData("ABCDE", nameof(SlotWin.SequenceUp))]
    [InlineData("789AB", nameof(SlotWin.SequenceUp))]
    [InlineData("VWXYZ", nameof(SlotWin.SequenceUp))]
    [InlineData("43210", nameof(SlotWin.SequenceDown))]
    [InlineData("BA987", nameof(SlotWin.SequenceDown))]
    [InlineData("YZ012", nameof(SlotWin.None))]
    [InlineData("ZK8A0", nameof(SlotWin.None))]
    [InlineData("AABBC", nameof(SlotWin.None))]
    [InlineData("ABCDD", nameof(SlotWin.None))]
    [InlineData("abcde", nameof(SlotWin.None))]
    public void ClassifiesSpins(string text, string expected)
    {
        Assert.Equal(expected, SlotRules.Classify(text).ToString());
    }

    [Theory]
    [InlineData("A1B2C", true)]
    [InlineData("a1b2c", false)]
    [InlineData("ABCD", false)]
    [InlineData("ABCDEF", false)]
    [InlineData("AB-CD", false)]
    [InlineData(null, false)]
    public void ValidatesSymbols(string? text, bool expected)
    {
        Assert.Equal(expected, SlotRules.IsValid(text));
    }

    [Theory]
    [InlineData("XXXXB", true)]
    [InlineData("KKKKK", true)]
    [InlineData("7Q7A7", false)]
    [InlineData("ABCDE", true)]
    [InlineData("43210", true)]
    [InlineData("VWXYZ", true)]
    [InlineData("13AKZ", false)]
    [InlineData("YZ012", false)]
    [InlineData("ZK8A0", false)]
    [InlineData("kkkkk", false)]
    [InlineData(null, false)]
    public void KnowsJackpots(string? text, bool expected)
    {
        Assert.Equal(expected, SlotRules.IsJackpot(text));
    }

    [Theory]
    [InlineData("7", true)]
    [InlineData("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a", false)]
    [InlineData("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0", false)]
    public void ValidatesDrawAlphabets(string? alphabet, bool expected)
    {
        Assert.Equal(expected, SlotRules.IsAlphabet(alphabet));
    }

    [Fact]
    public void DrawsFromTheGivenAlphabet()
    {
        Assert.Equal("77777", SlotRules.Draw("7"));
    }

    [Fact]
    public void DrawsValidSpins()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.True(SlotRules.IsValid(SlotRules.Draw()));
        }
    }

    [Fact]
    public void MarksWinningSymbols()
    {
        Assert.Equal([true, false, true, false, true], SlotRules.Hits("7Q7A7"));
        Assert.Equal([true, true, true, true, true], SlotRules.Hits("ABCDE"));
        Assert.Equal([true, true, true, true, true], SlotRules.Hits("43210"));
        Assert.Equal([false, false, false, false, false], SlotRules.Hits("AABBC"));
    }

    [Fact]
    public void NeighboursWrapAroundTheStrip()
    {
        Assert.Equal('0', SlotRules.Above('Z'));
        Assert.Equal('Z', SlotRules.Below('0'));
        Assert.Equal('A', SlotRules.Above('9'));
    }

    [Fact]
    public void EveryWinHasALabel()
    {
        foreach (var win in Enum.GetValues<SlotWin>())
        {
            Assert.Equal(win is SlotWin.None, SlotRules.Label(win).Length == 0);
        }
    }
}
