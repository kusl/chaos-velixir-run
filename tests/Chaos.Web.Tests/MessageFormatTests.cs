using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class MessageFormatTests
{
    private static readonly Guid Id = Guid.CreateVersion7(new DateTimeOffset(2026, 10, 12, 9, 5, 7, 250, TimeSpan.Zero));

    [Fact]
    public void IsoMatchesJavaScriptToIsoString()
    {
        Assert.Equal("2026-10-12T09:05:07.250Z", MessageFormat.Iso(Id));
    }

    [Fact]
    public void LabelShowsTimeForToday()
    {
        Assert.Equal("09:05:07", MessageFormat.Label(Id, new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void LabelShowsDateForOtherDays()
    {
        Assert.Equal("2026-10-12 09:05", MessageFormat.Label(Id, new DateTimeOffset(2026, 10, 13, 0, 0, 1, TimeSpan.Zero)));
    }
}
