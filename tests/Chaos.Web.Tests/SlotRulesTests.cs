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
