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

    public PostResult Post(string? input, string client) => Submit(input, client, null).Result;

    public PostReply Submit(string? input, string client, IEnumerable<MetaEntry?>? meta)
    {
        var text = MessageText.Normalize(input);
        if (text.Length == 0)
        {
            return new PostReply(PostResult.Empty);
        }

        if (!_posts.TryTake(client))
        {
            return new PostReply(PostResult.TooFast);
        }

        lock (_gate)
        {
            var window = Math.Min(RepeatWindow, _messages.Length);
            for (var i = 0; i < window; i++)
            {
                if (string.Equals(_messages[i].Text, text, StringComparison.Ordinal))
                {
                    return new PostReply(PostResult.Repeat);
                }
            }

            var message = new BoardMessage(Guid.CreateVersion7(_time.GetUtcNow()), text, MessageMeta.Normalize(meta));
            Merge([message]);
            return new PostReply(PostResult.Accepted, message);
        }
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

    private bool IsAcceptable(BoardMessage message, DateTimeOffset now) =>
        message is not null
        && MessageWindow.IsRestorable(message.Id, now, StartedAt)
        && MessageText.IsNormalized(message.Text)
        && MessageMeta.IsNormalized(message.Meta);

    private int Merge(IReadOnlyCollection<BoardMessage> candidates)
    {
        var change = MessageWindow.Merge(_messages, candidates, Capacity);
        if (change is null)
        {
            return 0;
        }

        var previous = _version;
        _version++;
        Volatile.Write(ref _messages, change.Next);
        Broadcast(BoardUpdate.Delta(_version, previous, change.Added, change.Removed));
        return change.Added.Length;
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
