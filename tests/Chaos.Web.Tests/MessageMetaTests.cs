using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class MessageMetaTests
{
    private static readonly string[] Keys =
    [
        "latitude", "longitude", "accuracy", "altitude", "altitudeAccuracy", "heading", "speed", "positionAt",
        "timeZone", "utcOffset", "localTime", "locale", "calendar", "numberingSystem", "language", "languages",
        "userAgent", "brands", "platform", "mobile", "vendor", "webdriver", "doNotTrack", "globalPrivacyControl",
        "pdfViewer", "screen", "availableScreen", "viewport", "pixelRatio", "colorDepth", "orientation", "cores",
        "memory", "touchPoints", "pointer", "colorScheme", "reducedMotion", "contrast", "online", "connection",
        "downlink", "rtt", "saveData",
    ];

    [Fact]
    public void TheCatalogMatchesTheBrowserModule()
    {
        Assert.Equal(Keys, MessageMeta.Fields.Select(field => field.Key));
        Assert.Equal(Keys.Length, MessageMeta.Fields.Select(field => field.Label).Distinct().Count());
        Assert.Equal("Latitude", MessageMeta.LabelOf("latitude"));
        Assert.Equal("CPU threads", MessageMeta.LabelOf("cores"));
        Assert.Equal("mystery", MessageMeta.LabelOf("mystery"));
    }

    [Fact]
    public void NormalizeKeepsKnownKeysInCatalogOrder()
    {
        var normalized = MessageMeta.Normalize(
        [
            new("timeZone", " Asia/Kathmandu "),
            new("mystery", "dropped"),
            new("longitude", "85.324"),
            new("latitude", "27.7172"),
            new("latitude", "1"),
            null,
        ]);

        Assert.NotNull(normalized);
        Assert.Equal(
            [new MetaEntry("latitude", "27.7172"), new MetaEntry("longitude", "85.324"), new MetaEntry("timeZone", "Asia/Kathmandu")],
            normalized);
    }

    [Fact]
    public void NormalizeNeedsLatitudeAndLongitude()
    {
        Assert.Null(MessageMeta.Normalize(null));
        Assert.Null(MessageMeta.Normalize([new("latitude", "1")]));
        Assert.Null(MessageMeta.Normalize([new("latitude", "91"), new("longitude", "1")]));
        Assert.Null(MessageMeta.Normalize([new("latitude", "1"), new("longitude", "NaN")]));
        Assert.Null(MessageMeta.Normalize([new("latitude", "north"), new("longitude", "1")]));
        Assert.NotNull(MessageMeta.Normalize([new("latitude", "-90"), new("longitude", "180")]));
        Assert.NotNull(MessageMeta.Normalize([new("latitude", "1e-7"), new("longitude", "-0.5")]));
    }

    [Fact]
    public void NormalizeDropsOutOfRangeNumbersAndBlankText()
    {
        var normalized = MessageMeta.Normalize(
        [
            new("latitude", "1"),
            new("longitude", "2"),
            new("accuracy", "-1"),
            new("heading", "361"),
            new("cores", "8"),
            new("language", "   "),
            new("platform", "\u0000\u0007"),
        ]);

        Assert.Equal(["latitude", "longitude", "cores"], normalized!.Select(entry => entry.Key));
    }

    [Fact]
    public void NormalizeStripsControlCharactersAndCutsLongValues()
    {
        var agent = new string('a', 600);
        var normalized = MessageMeta.Normalize(
        [
            new("latitude", "1"),
            new("longitude", "2"),
            new("userAgent", agent),
            new("vendor", "Goo\u0000gle\nInc."),
            new("utcOffset", "+05:45:00:00"),
            new("timeZone", new string('x', 63) + "😀"),
        ]);

        var values = normalized!.ToDictionary(entry => entry.Key, entry => entry.Value);
        Assert.Equal(512, values["userAgent"].Length);
        Assert.Equal("GoogleInc.", values["vendor"]);
        Assert.Equal("+05:45:0", values["utcOffset"]);
        Assert.Equal(new string('x', 63), values["timeZone"]);
    }

    [Fact]
    public void NormalizeIgnoresNullValuesAndEndlessInput()
    {
        var flood = Enumerable.Range(0, 10_000).Select(i => new MetaEntry($"k{i}", "v")).Prepend(new MetaEntry("latitude", "1"));
        Assert.Null(MessageMeta.Normalize(flood.Append(new MetaEntry("longitude", "2"))));
        Assert.Null(MessageMeta.Normalize([new("latitude", null!), new("longitude", "2")]));
    }

    [Fact]
    public void IsNormalizedAcceptsOnlyTheCanonicalForm()
    {
        MetaEntry[] canonical = [new("latitude", "1"), new("longitude", "2"), new("timeZone", "UTC")];
        Assert.True(MessageMeta.IsNormalized(null));
        Assert.True(MessageMeta.IsNormalized(canonical));
        Assert.False(MessageMeta.IsNormalized([]));
        Assert.False(MessageMeta.IsNormalized([new("longitude", "2"), new("latitude", "1")]));
        Assert.False(MessageMeta.IsNormalized([new("latitude", "1"), new("longitude", "2"), new("timeZone", " UTC")]));
        Assert.False(MessageMeta.IsNormalized([new("latitude", "1"), new("longitude", "2"), new("mystery", "x")]));
    }
}
