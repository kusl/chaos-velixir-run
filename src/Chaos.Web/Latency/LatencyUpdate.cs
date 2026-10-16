using Chaos.Web.Messages;

namespace Chaos.Web.Latency;

internal sealed record LatencyUpdate(
    string Kind,
    long Version,
    long Previous,
    IReadOnlyList<BoardMessage> Added,
    IReadOnlyList<Guid> Removed,
    bool Running)
{
    public static LatencyUpdate Snapshot(long version, IReadOnlyList<BoardMessage> messages, bool running) =>
        new("snapshot", version, version, messages, [], running);

    public static LatencyUpdate Delta(long version, long previous, IReadOnlyList<BoardMessage> added, IReadOnlyList<Guid> removed, bool running) =>
        new("delta", version, previous, added, removed, running);

    public static LatencyUpdate State(long version, bool running) => new("state", version, version, [], [], running);
}
