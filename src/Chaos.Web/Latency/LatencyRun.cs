using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Chaos.Web.Latency;

internal sealed record LatencyResult(string Key, int? Browser, int? Server);

internal sealed record LatencySummary(int? Browser, int? Server, int Answered, int Total);

internal static class LatencyRun
{
    public const int MaxMilliseconds = 60_000;
    public const int MaxEntries = 64;
    public const int MaxKeyLength = 32;
    public const int MaxLength = 4096;

    public static string Compose(IReadOnlyList<LatencyEndpoint> endpoints, IReadOnlyList<int?> browser, IReadOnlyList<int?> server)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(server);
        var results = new List<LatencyResult>(endpoints.Count);
        for (var i = 0; i < endpoints.Count; i++)
        {
            results.Add(new LatencyResult(
                endpoints[i].Key,
                i < browser.Count ? Clamp(browser[i]) : null,
                endpoints[i].FromServer && i < server.Count ? Clamp(server[i]) : null));
        }

        return Format(results);
    }

    public static string Format(IEnumerable<LatencyResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var builder = new StringBuilder();
        foreach (var result in results)
        {
            if (builder.Length > 0)
            {
                builder.Append(';');
            }

            builder.Append(result.Key).Append('=');
            if (result.Browser is { } browser)
            {
                builder.Append(browser.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(',');
            if (result.Server is { } server)
            {
                builder.Append(server.ToString(CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out IReadOnlyList<LatencyResult>? results)
    {
        results = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        var entries = text.Split(';');
        if (entries.Length > MaxEntries)
        {
            return false;
        }

        var parsed = new List<LatencyResult>(entries.Length);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                return false;
            }

            var key = entry[..equals];
            var values = entry[(equals + 1)..].Split(',');
            if (!IsKey(key) || !keys.Add(key) || values.Length != 2
                || !TryValue(values[0], out var browser) || !TryValue(values[1], out var server))
            {
                return false;
            }

            parsed.Add(new LatencyResult(key, browser, server));
        }

        results = parsed;
        return true;
    }

    public static bool IsValid([NotNullWhen(true)] string? text) =>
        TryParse(text, out var results) && string.Equals(Format(results), text, StringComparison.Ordinal);

    public static LatencySummary Summarize(IReadOnlyList<LatencyResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new LatencySummary(
            Median(results.Select(result => result.Browser)),
            Median(results.Select(result => result.Server)),
            results.Count(result => result.Browser is not null || result.Server is not null),
            results.Count);
    }

    public static int? Median(IEnumerable<int?> values)
    {
        var sorted = values.OfType<int>().Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (int)Math.Round((sorted[middle - 1] + sorted[middle]) / 2.0, MidpointRounding.AwayFromZero);
    }

    public static string Milliseconds(int? value) =>
        value is { } milliseconds ? milliseconds.ToString(CultureInfo.InvariantCulture) + " ms" : "–";

    private static int? Clamp(int? value) => value is { } milliseconds and >= 0 ? Math.Min(milliseconds, MaxMilliseconds) : null;

    private static bool IsKey(string key)
    {
        if (key.Length > MaxKeyLength)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValue(string value, out int? milliseconds)
    {
        milliseconds = null;
        if (value.Length == 0)
        {
            return true;
        }

        if (value.Length > 5 || !value.All(char.IsAsciiDigit)
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed > MaxMilliseconds)
        {
            return false;
        }

        milliseconds = parsed;
        return true;
    }
}
