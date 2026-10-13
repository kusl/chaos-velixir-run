using Chaos.Web.Drawing;
using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Chaos.Web.Slots;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by)
    {
        _now += by;
        _timestamp += by.Ticks;
    }
}

internal static class Limits
{
    public static IOptions<ChaosOptions> Create(Action<ChaosOptions>? configure = null)
    {
        var options = new ChaosOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}

internal sealed class BoardRecorder(bool accept = true) : IBoardSubscriber
{
    public List<BoardUpdate> Updates { get; } = [];

    public bool Deliver(BoardUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal sealed class SlotRecorder(bool accept = true) : ISlotSubscriber
{
    public List<SlotUpdate> Updates { get; } = [];

    public bool Deliver(SlotUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal sealed class CanvasRecorder(bool accept = true) : ICanvasSubscriber
{
    public List<CanvasUpdate> Updates { get; } = [];

    public bool Deliver(CanvasUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}
