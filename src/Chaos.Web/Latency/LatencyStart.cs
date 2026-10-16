using System.Globalization;

namespace Chaos.Web.Latency;

internal enum StartResult
{
    Accepted,
    Busy,
    Wait,
}

internal sealed record LatencyStart(StartResult Result, Guid Id, int Seconds)
{
    public static LatencyStart Busy { get; } = new(StartResult.Busy, Guid.Empty, 0);

    public static LatencyStart Started(Guid id, int cooldown) => new(StartResult.Accepted, id, cooldown);

    public static LatencyStart Wait(int seconds) => new(StartResult.Wait, Guid.Empty, seconds);

    public string Reply => Result switch
    {
        StartResult.Accepted => string.Create(CultureInfo.InvariantCulture, $"ok {Id:D} {Seconds}"),
        StartResult.Busy => "busy",
        _ => string.Create(CultureInfo.InvariantCulture, $"wait {Seconds}"),
    };
}
