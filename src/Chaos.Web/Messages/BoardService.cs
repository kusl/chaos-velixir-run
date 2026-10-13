using Chaos.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Messages;

internal sealed class BoardService
{
    public const int MaxOffers = 50;
    public const int RepeatWindow = 10;

    private readonly TimeProvider _time;
    private readonly KeyedTokenBuckets _posts;
    private readonly Lock _gate = new();
    private readonly HashSet<IBoardSubscriber> _subscribers = [];
    private BoardMessage[] _messages = [];
    private long _version;

    public BoardService(TimeProvider time, IOptions<ChaosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _time = time;
        Capacity = limits.BoardCapacity;
        _posts = new KeyedTokenBuckets(limits.PostBurst, limits.PostsPerSecond, time);
        StartedAt = time.GetUtcNow();
    }

    public int Capacity { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyList<BoardMessage> Messages => Volatile.Read(ref _messages);

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public PostResult Post(string? input, string client)
    {
        var text = MessageText.Normalize(input);
        if (text.Length == 0)
        {
            return PostResult.Empty;
        }

        if (!_posts.TryTake(client))
        {
            return PostResult.TooFast;
        }

        lock (_gate)
        {
            var window = Math.Min(RepeatWindow, _messages.Length);
            for (var i = 0; i < window; i++)
            {
                if (string.Equals(_messages[i].Text, text, StringComparison.Ordinal))
                {
                    return PostResult.Repeat;
                }
            }

            Merge([new BoardMessage(Guid.CreateVersion7(_time.GetUtcNow()), text)]);
        }

        return PostResult.Accepted;
    }

    public int Offer(IEnumerable<BoardMessage> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var now = _time.GetUtcNow();
        var valid = new List<BoardMessage>();
        foreach (var message in offered.Take(MaxOffers))
        {
            if (IsAcceptable(message, now))
            {
                valid.Add(message);
            }
        }

        if (valid.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            return Merge(valid);
        }
    }

    public void Subscribe(IBoardSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            if (subscriber.Deliver(BoardUpdate.Snapshot(_version, _messages)))
            {
                _subscribers.Add(subscriber);
            }
        }
    }

    public void Unsubscribe(IBoardSubscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public bool IsSubscribed(IBoardSubscriber subscriber)
    {
        lock (_gate)
        {
            return _subscribers.Contains(subscriber);
        }
    }

    private bool IsAcceptable(BoardMessage message, DateTimeOffset now)
    {
        if (message is null || !Uuid7.IsValid(message.Id))
        {
            return false;
        }

        var at = Uuid7.Timestamp(message.Id);
        return at <= now && at < StartedAt && MessageText.IsNormalized(message.Text);
    }

    private int Merge(IReadOnlyCollection<BoardMessage> candidates)
    {
        var current = _messages;
        var known = new HashSet<Guid>(current.Length);
        foreach (var message in current)
        {
            known.Add(message.Id);
        }

        var next = current
            .Concat(candidates.Where(message => !known.Contains(message.Id)).DistinctBy(message => message.Id))
            .OrderByDescending(message => message.Id)
            .Take(Capacity)
            .ToArray();
        var added = next.Where(message => !known.Contains(message.Id)).ToArray();
        if (added.Length == 0)
        {
            return 0;
        }

        var kept = next.Select(message => message.Id).ToHashSet();
        var removed = current.Where(message => !kept.Contains(message.Id)).Select(message => message.Id).ToArray();
        var previous = _version;
        _version++;
        Volatile.Write(ref _messages, next);
        Broadcast(BoardUpdate.Delta(_version, previous, added, removed));
        return added.Length;
    }

    private void Broadcast(BoardUpdate update)
    {
        List<IBoardSubscriber>? dropped = null;
        foreach (var subscriber in _subscribers)
        {
            if (!subscriber.Deliver(update))
            {
                (dropped ??= []).Add(subscriber);
            }
        }

        if (dropped is not null)
        {
            _subscribers.ExceptWith(dropped);
        }
    }
}
