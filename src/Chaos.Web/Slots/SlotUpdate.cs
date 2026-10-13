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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SlotSpin? Spin)
{
    public static SlotUpdate Snapshot(long version, IReadOnlyList<BoardMessage> messages, SlotSpin? spin) =>
        new("snapshot", version, version, messages, [], spin);

    public static SlotUpdate Delta(long version, long previous, IReadOnlyList<BoardMessage> added, IReadOnlyList<Guid> removed, SlotSpin? spin) =>
        new("delta", version, previous, added, removed, spin);
}
