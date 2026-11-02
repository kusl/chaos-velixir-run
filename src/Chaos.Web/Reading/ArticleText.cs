using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chaos.Web.Reading;

internal static class ArticleText
{
    public const int IdLength = 12;
    public const int MinRule = 20;
    public const int MaxHeaderLines = 6;
    public const int MaxTitleLength = 200;

    private const string Untitled = "Untitled";
    private const string SummaryMarker = "SUMMARY";
    private const string ArticleMarker = "ARTICLE";
    private const string BylinePrefix = "By ";
    private const string PublishedPrefix = "Published ";
    private static readonly string[] ListMarkers = ["- ", "\u2022 ", "* ", "\uf0b7 "];
    private static readonly SearchValues<byte> Blank = SearchValues.Create(" \t\r\n"u8);

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    public static bool IsId([NotNullWhen(true)] string? value) =>
        value is { Length: IdLength } && value.All(char.IsAsciiHexDigitLower);

    public static IReadOnlyList<Range> Records(ReadOnlySpan<byte> utf8)
    {
        var rules = new List<(int Line, int Start)>();
        var line = 0;
        var origin = utf8.StartsWith(Bom) ? Bom.Length : 0;
        var start = origin;
        while (start < utf8.Length)
        {
            var end = utf8[start..].IndexOf((byte)'\n');
            var next = end < 0 ? utf8.Length : start + end + 1;
            if (IsRule(utf8[start..(end < 0 ? utf8.Length : start + end)]))
            {
                rules.Add((line, start));
            }

            line++;
            start = next;
        }

        var opens = new List<int>();
        for (var i = 0; i + 1 < rules.Count; i++)
        {
            var gap = rules[i + 1].Line - rules[i].Line - 1;
            if (gap is >= 1 and <= MaxHeaderLines)
            {
                opens.Add(rules[i].Start);
                i++;
            }
        }

        if (opens.Count == 0)
        {
            return HasText(utf8[origin..]) ? [new Range(origin, utf8.Length)] : [];
        }

        var records = new List<Range>(opens.Count);
        for (var i = 0; i < opens.Count; i++)
        {
            records.Add(new Range(opens[i], i + 1 < opens.Count ? opens[i + 1] : utf8.Length));
        }

        return records;
    }

    public static Article? Parse(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var lines = record.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        string? slug = null;
        if (first < lines.Length && IsRule(lines[first]))
        {
            var close = Array.FindIndex(lines, first + 1, IsRule);
            if (close > first)
            {
                slug = lines[(first + 1)..close].Select(header => header.Trim()).FirstOrDefault(header => header.Length > 0);
                first = close + 1;
            }
        }

        var paragraphs = Paragraphs(lines.AsSpan(first));
        var summaryAt = paragraphs.FindIndex(paragraph => IsMarker(paragraph, SummaryMarker));
        var articleAt = paragraphs.FindIndex(paragraph => IsMarker(paragraph, ArticleMarker));

        string? summary = null;
        List<List<string>> content;
        if (articleAt >= 0)
        {
            if (summaryAt >= 0 && summaryAt < articleAt)
            {
                summary = Joined(paragraphs[(summaryAt + 1)..articleAt].SelectMany(paragraph => paragraph));
            }

            content = paragraphs[(articleAt + 1)..];
        }
        else
        {
            content = [.. paragraphs.Where(paragraph => !IsMarker(paragraph, SummaryMarker))];
        }

        string? title = null;
        string? dek = null;
        string? byline = null;
        DateTimeOffset? published = null;
        Uri? source = null;
        if (articleAt >= 0 && content.Count > 0)
        {
            (title, dek, byline, published, source) = Head(content[0]);
            content = content[1..];
        }
        else if (content.Count > 1 && content[0] is [var only] && only.Length <= MaxTitleLength)
        {
            title = only;
            content = content[1..];
        }

        if (content.Count == 0)
        {
            return null;
        }

        title ??= TitleOf(slug) ?? Untitled;
        var blocks = content.Select(Block).ToArray();
        var key = source?.AbsoluteUri ?? title + "\n" + string.Join("\n\n", blocks.Select(block => block.Text));
        return new Article(IdOf(key), title, dek, byline, published, source, summary, blocks);
    }

    public static string IdOf(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..IdLength];

    public static string? TitleOf(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(slug.Trim());
        var words = name.Replace('-', ' ').Replace('_', ' ').Trim();
        if (words.Length == 0)
        {
            return null;
        }

        return string.Concat(char.ToUpperInvariant(words[0]).ToString(), words[1..]);
    }

    private static (string Title, string? Dek, string? Byline, DateTimeOffset? Published, Uri? Source) Head(List<string> head)
    {
        var end = head.Count;
        Uri? source = null;
        if (end > 1 && Uri.TryCreate(head[end - 1], UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            source = uri;
            end--;
        }

        DateTimeOffset? published = null;
        if (end > 1 && head[end - 1].StartsWith(PublishedPrefix, StringComparison.Ordinal))
        {
            published = PublishedOf(head[end - 1][PublishedPrefix.Length..]);
            end--;
        }

        string? byline = null;
        if (end > 1 && head[end - 1].StartsWith(BylinePrefix, StringComparison.Ordinal))
        {
            byline = head[end - 1];
            end--;
        }

        var dek = end > 1 ? Joined(head[1..end]) : null;
        return (head[0], dek, byline, published, source);
    }

    private static DateTimeOffset? PublishedOf(string text) =>
        DateTimeOffset.TryParseExact(text.Trim(), "yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    private static ArticleBlock Block(List<string> lines)
    {
        if (lines.All(IsListItem))
        {
            return new ArticleBlock([.. lines.Select(line => line[2..].Trim())], true);
        }

        return new ArticleBlock(lines, false);
    }

    private static bool IsListItem(string line) =>
        line.Length > 2 && ListMarkers.Any(marker => line.StartsWith(marker, StringComparison.Ordinal));

    private static List<List<string>> Paragraphs(ReadOnlySpan<string> lines)
    {
        var paragraphs = new List<List<string>>();
        List<string>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                current = null;
                continue;
            }

            if (IsRule(line))
            {
                continue;
            }

            if (current is null)
            {
                current = [];
                paragraphs.Add(current);
            }

            current.Add(line);
        }

        return paragraphs;
    }

    private static string? Joined(IEnumerable<string> lines)
    {
        var text = string.Join(' ', lines).Trim();
        return text.Length > 0 ? text : null;
    }

    private static bool IsMarker(List<string> paragraph, string marker) =>
        paragraph is [var only] && string.Equals(only, marker, StringComparison.Ordinal);

    private static bool IsRule(string line)
    {
        var text = line.AsSpan().Trim();
        return text.Length >= MinRule && !text.ContainsAnyExcept('=');
    }

    private static bool IsRule(ReadOnlySpan<byte> line)
    {
        var text = line.TrimEnd((byte)'\r');
        return text.Length >= MinRule && !text.ContainsAnyExcept((byte)'=');
    }

    private static bool HasText(ReadOnlySpan<byte> utf8) =>
        utf8.ContainsAnyExcept(Blank);
}
