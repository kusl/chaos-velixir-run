using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Chaos.Web.Slots;

internal static class SlotRules
{
    public const int Length = 5;
    public const string Symbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Draw() => Draw(Symbols);

    public static string Draw(string alphabet) => RandomNumberGenerator.GetString(alphabet, Length);

    public static bool IsAlphabet([NotNullWhen(true)] string? alphabet) =>
        !string.IsNullOrEmpty(alphabet) && alphabet.Length <= Symbols.Length && alphabet.All(c => Symbols.Contains(c, StringComparison.Ordinal));

    public static bool IsJackpot(SlotWin win) => win is SlotWin.FourOfAKind or SlotWin.FiveOfAKind;

    public static bool IsJackpot(string? text) => IsJackpot(Classify(text));

    public static bool IsValid([NotNullWhen(true)] string? text)
    {
        if (text is null || text.Length != Length)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c) && !char.IsAsciiLetterUpper(c))
            {
                return false;
            }
        }

        return true;
    }

    public static SlotWin Classify(string? text)
    {
        if (!IsValid(text))
        {
            return SlotWin.None;
        }

        var most = text.GroupBy(c => c).Max(group => group.Count());
        if (most >= 5)
        {
            return SlotWin.FiveOfAKind;
        }

        if (most == 4)
        {
            return SlotWin.FourOfAKind;
        }

        if (most == 3)
        {
            return SlotWin.ThreeOfAKind;
        }

        if (IsOrdered(text, 1))
        {
            return SlotWin.StraightUp;
        }

        return IsOrdered(text, -1) ? SlotWin.StraightDown : SlotWin.None;
    }

    public static bool[] Hits(string? text)
    {
        var hits = new bool[Length];
        if (!IsValid(text))
        {
            return hits;
        }

        var win = Classify(text);
        if (win is SlotWin.None)
        {
            return hits;
        }

        if (win is SlotWin.StraightUp or SlotWin.StraightDown)
        {
            Array.Fill(hits, true);
            return hits;
        }

        var top = text.GroupBy(c => c).MaxBy(group => group.Count())!.Key;
        for (var i = 0; i < Length; i++)
        {
            hits[i] = text[i] == top;
        }

        return hits;
    }

    public static string Label(SlotWin win) => win switch
    {
        SlotWin.ThreeOfAKind => "Three of a kind",
        SlotWin.FourOfAKind => "Four of a kind",
        SlotWin.FiveOfAKind => "Five of a kind",
        SlotWin.StraightUp => "Straight up",
        SlotWin.StraightDown => "Straight down",
        _ => "",
    };

    public static char Above(char symbol) => Symbols[(Symbols.IndexOf(symbol, StringComparison.Ordinal) + 1) % Symbols.Length];

    public static char Below(char symbol) => Symbols[(Symbols.IndexOf(symbol, StringComparison.Ordinal) + Symbols.Length - 1) % Symbols.Length];

    private static bool IsOrdered(string text, int direction)
    {
        for (var i = 1; i < text.Length; i++)
        {
            if ((text[i] - text[i - 1]) * direction <= 0)
            {
                return false;
            }
        }

        return true;
    }
}
