using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Slots;

internal sealed class SlotService
{
    public const int MaxOffers = 50;
    public const int MaxWinnerOffers = 500;
    public const int MaxJackpotOffers = 500;

    private readonly TimeProvider _time;
    private readonly KeyedTokenBuckets _presses;
    private readonly string _alphabet;
    private readonly Lock _gate = new();
    private readonly HashSet<ISlotSubscriber> _subscribers = [];
    private BoardMessage[] _messages = [];
    private BoardMessage[] _winners = [];
    private BoardMessage[] _jackpots = [];
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
        WinnerCapacity = limits.SlotWinnerCapacity;
        JackpotCapacity = limits.SlotJackpotCapacity;
        _alphabet = SlotRules.IsAlphabet(limits.SlotDrawSymbols) ? limits.SlotDrawSymbols : SlotRules.Symbols;
        SpinTime = TimeSpan.FromMilliseconds(limits.SpinMilliseconds);
        _presses = new KeyedTokenBuckets(limits.PressBurst, limits.PressesPerSecond, time);
        StartedAt = time.GetUtcNow();
    }

    public int Capacity { get; }

    public int WinnerCapacity { get; }

    public int JackpotCapacity { get; }

    public TimeSpan SpinTime { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyList<BoardMessage> Messages => Volatile.Read(ref _messages);

    public IReadOnlyList<BoardMessage> Winners => Volatile.Read(ref _winners);

    public IReadOnlyList<BoardMessage> Jackpots => Volatile.Read(ref _jackpots);

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

            var message = new BoardMessage(Guid.CreateVersion7(_time.GetUtcNow()), SlotRules.Draw(_alphabet));
            _spinId = message.Id;
            _spinStarted = now;
            Merge([message], new SlotSpin(message.Id, Milliseconds(SpinTime)), Lists.All, drawn: true);
            return SlotPress.Started(message.Id);
        }
    }

    public int Offer(IEnumerable<BoardMessage> offered) => Restore(offered, MaxOffers, Lists.All);

    public int OfferWinners(IEnumerable<BoardMessage> offered) => Restore(offered, MaxWinnerOffers, Lists.Winners | Lists.Jackpots);

    public int OfferJackpots(IEnumerable<BoardMessage> offered) => Restore(offered, MaxJackpotOffers, Lists.Jackpots);

    public void Subscribe(ISlotSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            if (subscriber.Deliver(SlotUpdate.Snapshot(_version, _messages, _winners, _jackpots, CurrentSpin(_time.GetTimestamp()))))
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

    private static bool IsWinner(BoardMessage message) => SlotRules.Classify(message.Text) is not SlotWin.None;

    private static bool IsJackpot(BoardMessage message) => SlotRules.IsJackpot(message.Text);

    private static bool Accepts(BoardMessage message, Lists lists) =>
        lists.HasFlag(Lists.Recent)
        || (lists.HasFlag(Lists.Winners) && IsWinner(message))
        || (lists.HasFlag(Lists.Jackpots) && IsJackpot(message));

    private int Restore(IEnumerable<BoardMessage> offered, int limit, Lists lists)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var now = _time.GetUtcNow();
        var valid = offered
            .Take(limit)
            .Where(message => message is not null && MessageWindow.IsRestorable(message.Id, now, StartedAt) && SlotRules.IsValid(message.Text))
            .Where(message => Accepts(message, lists))
            .ToList();
        if (valid.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            return Merge(valid, null, lists, drawn: false);
        }
    }

    private SlotSpin? CurrentSpin(long now)
    {
        if (_spinStarted is not { } started)
        {
            return null;
        }

        var remaining = SpinTime - _time.GetElapsedTime(started, now);
        return remaining > TimeSpan.Zero ? new SlotSpin(_spinId, Milliseconds(remaining)) : null;
    }

    private WindowChange? Enshrine(IEnumerable<BoardMessage> candidates, bool drawn)
    {
        var known = _jackpots.Select(message => message.Id).ToHashSet();
        var fresh = candidates
            .Where(message => IsJackpot(message) && !known.Contains(message.Id))
            .DistinctBy(message => message.Id)
            .Take(drawn ? int.MaxValue : Math.Max(0, JackpotCapacity - _jackpots.Length))
            .ToList();
        return fresh.Count == 0 ? null : MessageWindow.Merge(_jackpots, fresh, int.MaxValue);
    }

    private int Merge(IReadOnlyCollection<BoardMessage> candidates, SlotSpin? spin, Lists lists, bool drawn)
    {
        var change = lists.HasFlag(Lists.Recent) ? MessageWindow.Merge(_messages, candidates, Capacity) : null;
        var crowned = lists.HasFlag(Lists.Winners) ? MessageWindow.Merge(_winners, candidates.Where(IsWinner), WinnerCapacity) : null;
        var enshrined = lists.HasFlag(Lists.Jackpots) ? Enshrine(candidates, drawn) : null;
        if (change is null && crowned is null && enshrined is null)
        {
            return 0;
        }

        var previous = _version;
        _version++;
        if (change is not null)
        {
            Volatile.Write(ref _messages, change.Next);
        }

        if (crowned is not null)
        {
            Volatile.Write(ref _winners, crowned.Next);
        }

        if (enshrined is not null)
        {
            Volatile.Write(ref _jackpots, enshrined.Next);
        }

        Broadcast(SlotUpdate.Delta(
            _version,
            previous,
            change?.Added ?? [],
            change?.Removed ?? [],
            spin,
            crowned?.Added ?? [],
            crowned?.Removed ?? [],
            enshrined?.Added ?? []));
        return (change?.Added ?? [])
            .Concat(crowned?.Added ?? [])
            .Concat(enshrined?.Added ?? [])
            .Select(message => message.Id)
            .Distinct()
            .Count();
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

    [Flags]
    private enum Lists
    {
        None = 0,
        Recent = 1,
        Winners = 2,
        Jackpots = 4,
        All = Recent | Winners | Jackpots,
    }
}
