using System.Globalization;
using System.Text;

namespace Chaos.Web.Messages;

internal static class MessageMeta
{
    public const int MaxValueLength = 120;

    public static IReadOnlyList<MetaField> Fields { get; } =
    [
        new("latitude", "Latitude", 32, -90, 90),
        new("longitude", "Longitude", 32, -180, 180),
        new("accuracy", "Accuracy (m)", 32, 0, 1e8),
        new("altitude", "Altitude (m)", 32, -1e5, 1e6),
        new("altitudeAccuracy", "Altitude accuracy (m)", 32, 0, 1e8),
        new("heading", "Heading (°)", 32, 0, 360),
        new("speed", "Speed (m/s)", 32, 0, 1e5),
        new("positionAt", "Position time", 40),
        new("timeZone", "Time zone", 64),
        new("utcOffset", "UTC offset", 8),
        new("localTime", "Local time", 40),
        new("locale", "Locale", 64),
        new("calendar", "Calendar", 32),
        new("numberingSystem", "Numbering system", 32),
        new("language", "Language", 64),
        new("languages", "Languages", 200),
        new("userAgent", "User agent", 512),
        new("brands", "Brands", 200),
        new("platform", "Platform", 64),
        new("mobile", "Mobile", 8),
        new("vendor", "Vendor", 64),
        new("webdriver", "Automated", 8),
        new("doNotTrack", "Do Not Track", 16),
        new("globalPrivacyControl", "Global Privacy Control", 8),
        new("pdfViewer", "PDF viewer", 8),
        new("screen", "Screen", 24),
        new("availableScreen", "Available screen", 24),
        new("viewport", "Viewport", 24),
        new("pixelRatio", "Pixel ratio", 16, 0, 100),
        new("colorDepth", "Color depth", 8, 0, 128),
        new("orientation", "Orientation", 32),
        new("cores", "CPU threads", 8, 0, 4096),
        new("memory", "Memory (GB)", 8, 0, 4096),
        new("touchPoints", "Touch points", 8, 0, 1024),
        new("pointer", "Pointer", 16),
        new("colorScheme", "Color scheme", 16),
        new("reducedMotion", "Reduced motion", 8),
        new("contrast", "Contrast", 16),
        new("online", "Online", 8),
        new("connection", "Connection", 32),
        new("downlink", "Downlink (Mb/s)", 16, 0, 1e6),
        new("rtt", "Round trip (ms)", 16, 0, 1e6),
        new("saveData", "Data saver", 8),
    ];

    private static readonly Dictionary<string, int> Order = Fields
        .Select((field, index) => (field.Key, index))
        .ToDictionary(pair => pair.Key, pair => pair.index, StringComparer.Ordinal);

    public static string LabelOf(string key) =>
        Order.TryGetValue(key, out var index) ? Fields[index].Label : key;

    public static MetaEntry[]? Normalize(IEnumerable<MetaEntry?>? entries)
    {
        if (entries is null)
        {
            return null;
        }

        var values = new string?[Fields.Count];
        foreach (var entry in entries.Take(Fields.Count * 2))
        {
            if (entry?.Key is not { } key || !Order.TryGetValue(key, out var index) || values[index] is not null)
            {
                continue;
            }

            values[index] = Clean(Fields[index], entry.Value);
        }

        if (values[0] is null || values[1] is null)
        {
            return null;
        }

        var normalized = new List<MetaEntry>(Fields.Count);
        for (var i = 0; i < Fields.Count; i++)
        {
            if (values[i] is { } value)
            {
                normalized.Add(new MetaEntry(Fields[i].Key, value));
            }
        }

        return [.. normalized];
    }

    public static bool IsNormalized(IReadOnlyList<MetaEntry>? entries)
    {
        if (entries is null)
        {
            return true;
        }

        var normalized = Normalize(entries);
        return normalized is not null && normalized.SequenceEqual(entries);
    }

    private static string? Clean(MetaField field, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(input.Length, field.MaxLength));
        foreach (var c in input)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var text = builder.ToString().Trim();
        if (text.Length > field.MaxLength)
        {
            var cut = field.MaxLength;
            if (char.IsHighSurrogate(text[cut - 1]))
            {
                cut--;
            }

            text = text[..cut].TrimEnd();
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (!field.IsNumber)
        {
            return text;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            && number >= (field.Min ?? double.MinValue)
            && number <= (field.Max ?? double.MaxValue)
            ? text
            : null;
    }
}
