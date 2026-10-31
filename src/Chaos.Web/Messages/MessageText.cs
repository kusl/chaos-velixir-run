using System.Globalization;
using System.Text.RegularExpressions;

namespace Chaos.Web.Messages;

internal static partial class MessageText
{
    public const int MaxLength = 500;

    public static string Normalize(string? input, int maxLength = MaxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var text = string.Concat(input.ReplaceLineEndings("\n").Where(c => c == '\n' || !char.IsControl(c)));
        text = ExtraBlankLines().Replace(text, "\n\n").Trim();

        if (text.Length > maxLength)
        {
            var cut = 0;
            while (cut < text.Length)
            {
                var next = StringInfo.GetNextTextElementLength(text, cut);
                if (cut + next > maxLength)
                {
                    break;
                }

                cut += next;
            }

            text = text[..cut].TrimEnd();
        }

        return text.Any(IsVisible) ? text : "";
    }

    public static bool IsNormalized(string? text, int maxLength = MaxLength) =>
        !string.IsNullOrEmpty(text) && string.Equals(Normalize(text, maxLength), text, StringComparison.Ordinal);

    private static bool IsVisible(char c) =>
        !char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format;

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
