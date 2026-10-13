using Chaos.Web.Messages;

namespace Chaos.Web.Tests;

public sealed class Uuid7Tests
{
    [Fact]
    public void ReadsTheTimestampBack()
    {
        var at = new DateTimeOffset(2026, 10, 12, 23, 18, 42, 123, TimeSpan.Zero);
        var id = Guid.CreateVersion7(at);
        Assert.True(Uuid7.IsValid(id));
        Assert.Equal(at, Uuid7.Timestamp(id));
    }

    [Fact]
    public void ReadsAHandWrittenId()
    {
        var id = Guid.Parse("016f5e66-e800-7000-8000-000000000001");
        Assert.True(Uuid7.IsValid(id));
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Uuid7.Timestamp(id));
    }

    [Fact]
    public void RejectsOtherVersions()
    {
        Assert.False(Uuid7.IsValid(Guid.NewGuid()));
        Assert.False(Uuid7.IsValid(Guid.Empty));
    }

    [Fact]
    public void GuidOrderMatchesStringOrder()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 300).Select(i => Guid.CreateVersion7(start.AddMilliseconds(i * 7919 % 1000))).ToList();
        var byGuid = ids.Order().Select(id => id.ToString()).ToList();
        var byString = ids.Select(id => id.ToString()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(byString, byGuid);
    }
}
