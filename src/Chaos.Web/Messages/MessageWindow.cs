namespace Chaos.Web.Messages;

internal sealed record WindowChange(BoardMessage[] Next, BoardMessage[] Added, Guid[] Removed);

internal static class MessageWindow
{
    public static WindowChange? Merge(BoardMessage[] current, IEnumerable<BoardMessage> candidates, int capacity)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);
        var known = new HashSet<Guid>(current.Length);
        foreach (var message in current)
        {
            known.Add(message.Id);
        }

        var next = current
            .Concat(candidates.Where(message => !known.Contains(message.Id)).DistinctBy(message => message.Id))
            .OrderByDescending(message => message.Id)
            .Take(capacity)
            .ToArray();
        var added = next.Where(message => !known.Contains(message.Id)).ToArray();
        if (added.Length == 0)
        {
            return null;
        }

        var kept = next.Select(message => message.Id).ToHashSet();
        var removed = current.Where(message => !kept.Contains(message.Id)).Select(message => message.Id).ToArray();
        return new WindowChange(next, added, removed);
    }

    public static bool IsRestorable(Guid id, DateTimeOffset now, DateTimeOffset startedAt)
    {
        if (!Uuid7.IsValid(id))
        {
            return false;
        }

        var at = Uuid7.Timestamp(id);
        return at <= now && at < startedAt;
    }
}
