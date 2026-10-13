using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Slots;

internal sealed class SlotService
{
    public const int MaxOffers = 50;

    private readonly TimeProvider _time;
    private readonly KeyedTokenBuckets _presses;
    private readonly Lock _gate = new();
    private readonly HashSet<ISlotSubscriber> _subscribers = [];
    private BoardMessage[] _messages = [];
    private long _version;
    private Guid _spinId;
    private long? _spinStarted;

    public SlotService(TimeProvider time, IOptions<ChaosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _time = time;
        Capacity = limits.SlotCapacity;
        SpinTime = TimeSpan.FromMilliseconds(limits.SpinMilliseconds);
        _presses = new KeyedTokenBuckets(limits.PressBurst, limits.PressesPerSecond, time);
        StartedAt = time.GetUtcNow();
    }

    public int Capacity { get; }

    public TimeSpan SpinTime { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyList<BoardMessage> Messages => Volatile.Read(ref _messages);

    public SlotSpin? Spinning
    {
        get
        {
            lock (_gate)
            {
                return CurrentSpin(_time.GetTimestamp());
            }
        }
    }

    public SlotPress Press(string client)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (CurrentSpin(now) is not null)
            {
                return SlotPress.Busy;
            }

            if (!_presses.TryTake(client))
            {
                return SlotPress.TooFast;
            }

            var message = new BoardMessage(Guid.CreateVersion7(_time.GetUtcNow()), SlotRules.Draw());
            _spinId = message.Id;
            _spinStarted = now;
            Merge([message], new SlotSpin(message.Id, Milliseconds(SpinTime)));
            return SlotPress.Started(message.Id);
        }
    }

    public int Offer(IEnumerable<BoardMessage> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var now = _time.GetUtcNow();
        var valid = offered
            .Take(MaxOffers)
            .Where(message => message is not null && MessageWindow.IsRestorable(message.Id, now, StartedAt) && SlotRules.IsValid(message.Text))
            .ToList();
        if (valid.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            return Merge(valid, null);
        }
    }

    public void Subscribe(ISlotSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            if (subscriber.Deliver(SlotUpdate.Snapshot(_version, _messages, CurrentSpin(_time.GetTimestamp()))))
            {
                _subscribers.Add(subscriber);
            }
        }
    }

    public void Unsubscribe(ISlotSubscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public bool IsSubscribed(ISlotSubscriber subscriber)
    {
        lock (_gate)
        {
            return _subscribers.Contains(subscriber);
        }
    }

    private static int Milliseconds(TimeSpan span) => (int)Math.Ceiling(span.TotalMilliseconds);

    private SlotSpin? CurrentSpin(long now)
    {
        if (_spinStarted is not { } started)
        {
            return null;
        }

        var remaining = SpinTime - _time.GetElapsedTime(started, now);
        return remaining > TimeSpan.Zero ? new SlotSpin(_spinId, Milliseconds(remaining)) : null;
    }

    private int Merge(IReadOnlyCollection<BoardMessage> candidates, SlotSpin? spin)
    {
        var change = MessageWindow.Merge(_messages, candidates, Capacity);
        if (change is null)
        {
            return 0;
        }

        var previous = _version;
        _version++;
        Volatile.Write(ref _messages, change.Next);
        Broadcast(SlotUpdate.Delta(_version, previous, change.Added, change.Removed, spin));
        return change.Added.Length;
    }

    private void Broadcast(SlotUpdate update)
    {
        List<ISlotSubscriber>? dropped = null;
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
