using System.Text.Json.Serialization;
using Chaos.Web.Messages;

namespace Chaos.Web.Slots;

internal sealed record SlotSpin(Guid Id, int Remaining);

internal sealed record SlotUpdate(
    string Kind,
    long Version,
    long Previous,
    IReadOnlyList<BoardMessage> Added,
    IReadOnlyList<Guid> Removed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SlotSpin? Spin,
    IReadOnlyList<BoardMessage> WinnersAdded,
    IReadOnlyList<Guid> WinnersRemoved)
{
    public static SlotUpdate Snapshot(long version, IReadOnlyList<BoardMessage> messages, IReadOnlyList<BoardMessage> winners, SlotSpin? spin) =>
        new("snapshot", version, version, messages, [], spin, winners, []);

    public static SlotUpdate Delta(
        long version,
        long previous,
        IReadOnlyList<BoardMessage> added,
        IReadOnlyList<Guid> removed,
        SlotSpin? spin,
        IReadOnlyList<BoardMessage> winnersAdded,
        IReadOnlyList<Guid> winnersRemoved) =>
        new("delta", version, previous, added, removed, spin, winnersAdded, winnersRemoved);
}
