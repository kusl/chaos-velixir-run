using System.Text.Json.Serialization;
using Chaos.Web.Messages;

namespace Chaos.Web.Latency;

internal sealed record LatencyUpdate(
    string Kind,
    long Version,
    long Previous,
    IReadOnlyList<BoardMessage> Added,
    IReadOnlyList<Guid> Removed,
    bool Running,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LatencyBoards? Boards)
{
    public static LatencyUpdate Snapshot(long version, IReadOnlyList<BoardMessage> messages, bool running, LatencyBoards boards) =>
        new("snapshot", version, version, messages, [], running, boards);

    public static LatencyUpdate Delta(long version, long previous, IReadOnlyList<BoardMessage> added, IReadOnlyList<Guid> removed, bool running, LatencyBoards? boards = null) =>
        new("delta", version, previous, added, removed, running, boards);

    public static LatencyUpdate State(long version, bool running) => new("state", version, version, [], [], running, null);
}
