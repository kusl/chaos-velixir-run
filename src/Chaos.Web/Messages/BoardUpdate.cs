namespace Chaos.Web.Messages;

internal sealed record BoardUpdate(string Kind, long Version, long Previous, IReadOnlyList<BoardMessage> Added, IReadOnlyList<Guid> Removed)
{
    public static BoardUpdate Snapshot(long version, IReadOnlyList<BoardMessage> messages) =>
        new("snapshot", version, version, messages, []);

    public static BoardUpdate Delta(long version, long previous, IReadOnlyList<BoardMessage> added, IReadOnlyList<Guid> removed) =>
        new("delta", version, previous, added, removed);
}
