namespace Chaos.Web.Slots;

internal enum PressResult
{
    Accepted,
    Busy,
    TooFast,
}

internal sealed record SlotPress(PressResult Result, Guid Id)
{
    public static SlotPress Busy { get; } = new(PressResult.Busy, Guid.Empty);

    public static SlotPress TooFast { get; } = new(PressResult.TooFast, Guid.Empty);

    public static SlotPress Started(Guid id) => new(PressResult.Accepted, id);

    public string Reply => Result switch
    {
        PressResult.Accepted => Id.ToString(),
        PressResult.Busy => "busy",
        _ => "slow",
    };
}
