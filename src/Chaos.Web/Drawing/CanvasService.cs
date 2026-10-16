using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Drawing;

internal sealed class CanvasService
{
    public const int MaxCoordinate = 4095;
    public const int MaxBatchPoints = 256;
    public const int MaxStrokePoints = 2048;
    public const int MaxOfferedStrokes = 500;

    public static readonly TimeSpan ResetCooldown = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly int _maxPoints;
    private readonly double _pointBurst;
    private readonly double _pointsPerSecond;
    private readonly KeyedTokenBuckets _resets;
    private readonly Lock _gate = new();
    private readonly LinkedList<Stroke> _strokes = new();
    private readonly HashSet<Guid> _ids = [];
    private readonly HashSet<ICanvasSubscriber> _subscribers = [];
    private int _epoch = 1;
    private int _points;
    private long? _lastReset;
    private Guid _resetId;
    private Guid _lastId;

    public CanvasService(TimeProvider time, IOptions<ChaosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _time = time;
        _maxPoints = limits.CanvasMaxPoints;
        _pointBurst = limits.StrokePointBurst;
        _pointsPerSecond = limits.StrokePointsPerSecond;
        _resets = new KeyedTokenBuckets(limits.ResetBurst, limits.ResetsPerSecond, time);
        StartedAt = time.GetUtcNow();
    }

    public DateTimeOffset StartedAt { get; }

    public int MaxPoints => _maxPoints;

    public int Epoch
    {
        get
        {
            lock (_gate)
            {
                return _epoch;
            }
        }
    }

    public Guid ResetId
    {
        get
        {
            lock (_gate)
            {
                return _resetId;
            }
        }
    }

    public int PointCount
    {
        get
        {
            lock (_gate)
            {
                return _points;
            }
        }
    }

    public int StrokeCount
    {
        get
        {
            lock (_gate)
            {
                return _strokes.Count;
            }
        }
    }

    public CanvasSession OpenSession(ICanvasSubscriber subscriber) =>
        new(subscriber, new TokenBucket(_pointBurst, _pointsPerSecond, _time));

    public DrawResult Draw(CanvasSession session, int epoch, int clientStroke, int[] points, bool end)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(points);

        if (points.Length % 2 != 0 || points.Length > MaxBatchPoints * 2 || (points.Length == 0 && !end) || !InRange(points))
        {
            return DrawResult.Invalid;
        }

        var count = points.Length / 2;
        lock (_gate)
        {
            if (epoch != _epoch)
            {
                return DrawResult.Stale;
            }

            var stroke = session.Open is { } open && open.Epoch == _epoch && open.ClientId == clientStroke ? open : null;
            if (stroke is null)
            {
                if (clientStroke <= session.LastClientId || count == 0)
                {
                    return DrawResult.Invalid;
                }
            }
            else if (stroke.Count + count > MaxStrokePoints)
            {
                FinishOpen(session);
                return DrawResult.TooLong;
            }

            if (!session.Bucket.TryTake(count))
            {
                return DrawResult.TooFast;
            }

            if (!MakeRoom(count))
            {
                return DrawResult.Full;
            }

            if (stroke is null)
            {
                FinishOpen(session);
                stroke = new Stroke(Mint(), _epoch, clientStroke);
                _strokes.AddLast(stroke);
                _ids.Add(stroke.Id);
                session.Open = stroke;
                session.LastClientId = clientStroke;
                session.LastStroke = stroke.Id;
            }

            var offset = stroke.Count;
            stroke.Append(points);
            _points += count;
            if (end)
            {
                stroke.Done = true;
                session.Open = null;
            }

            Broadcast(CanvasUpdate.ForStroke(_epoch, stroke.Id, offset, points, end), session.Subscriber);
            return DrawResult.Accepted;
        }
    }

    public ResetResult Reset(string client)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_lastReset is { } last && _time.GetElapsedTime(last, now) < ResetCooldown)
            {
                return ResetResult.TooFast;
            }

            if (!_resets.TryTake(client))
            {
                return ResetResult.TooFast;
            }

            _lastReset = now;
            _epoch++;
            _resetId = Mint();
            _strokes.Clear();
            _ids.Clear();
            _points = 0;
            Broadcast(CanvasUpdate.ForReset(_epoch, _resetId), null);
            return ResetResult.Accepted;
        }
    }

    public bool OfferReset(Guid reset)
    {
        if (!MessageWindow.IsRestorable(reset, _time.GetUtcNow(), StartedAt))
        {
            return false;
        }

        lock (_gate)
        {
            if (reset.CompareTo(_resetId) <= 0)
            {
                return false;
            }

            _resetId = reset;
            if (reset.CompareTo(_lastId) > 0)
            {
                _lastId = reset;
            }

            var node = _strokes.First;
            while (node is not null)
            {
                var next = node.Next;
                if (node.Value.Id.CompareTo(reset) <= 0)
                {
                    _points -= node.Value.Count;
                    _ids.Remove(node.Value.Id);
                    _strokes.Remove(node);
                }
                else
                {
                    node.Value.Done = true;
                }

                node = next;
            }

            _epoch++;
            Broadcast(Snapshot(), null);
            return true;
        }
    }

    public int OfferStrokes(IEnumerable<StrokeOffer> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var now = _time.GetUtcNow();
        var valid = offered
            .Take(MaxOfferedStrokes)
            .Where(offer => offer is not null && IsRestorable(offer, now))
            .DistinctBy(offer => offer.Id)
            .OrderByDescending(offer => offer.Id)
            .ToList();
        if (valid.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var added = new List<StrokeSnapshot>();
            foreach (var offer in valid)
            {
                var count = offer.Points.Count / 2;
                if (offer.Id.CompareTo(_resetId) <= 0 || _ids.Contains(offer.Id) || _points + count > _maxPoints)
                {
                    continue;
                }

                var stroke = new Stroke(offer.Id, _epoch, 0) { Done = true };
                stroke.Append(offer.Points);
                Insert(stroke);
                _points += count;
                if (offer.Id.CompareTo(_lastId) > 0)
                {
                    _lastId = offer.Id;
                }

                added.Add(new StrokeSnapshot(stroke.Id, stroke.ToArray(), true));
            }

            if (added.Count > 0)
            {
                Broadcast(CanvasUpdate.ForMerge(_epoch, added), null);
            }

            return added.Count;
        }
    }

    public void Abandon(CanvasSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            FinishOpen(session);
        }
    }

    public void Subscribe(ICanvasSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            if (subscriber.Deliver(Snapshot()))
            {
                _subscribers.Add(subscriber);
            }
        }
    }

    public void Unsubscribe(ICanvasSubscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public bool IsSubscribed(ICanvasSubscriber subscriber)
    {
        lock (_gate)
        {
            return _subscribers.Contains(subscriber);
        }
    }

    private static bool InRange(IReadOnlyList<int> points)
    {
        foreach (var value in points)
        {
            if ((uint)value > MaxCoordinate)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRestorable(StrokeOffer offer, DateTimeOffset now) =>
        offer.Points is { Count: > 0 } points
        && points.Count % 2 == 0
        && points.Count <= MaxStrokePoints * 2
        && InRange(points)
        && MessageWindow.IsRestorable(offer.Id, now, StartedAt);

    private CanvasUpdate Snapshot() =>
        CanvasUpdate.ForSnapshot(
            _epoch,
            [.. _strokes.Select(stroke => new StrokeSnapshot(stroke.Id, stroke.ToArray(), stroke.Done))],
            _resetId == Guid.Empty ? null : _resetId,
            StartedAt.ToUnixTimeMilliseconds(),
            Math.Max(0, _maxPoints - _points));

    private Guid Mint()
    {
        _lastId = Uuid7.After(_lastId, _time.GetUtcNow());
        return _lastId;
    }

    private void Insert(Stroke stroke)
    {
        var node = _strokes.First;
        while (node is not null && node.Value.Id.CompareTo(stroke.Id) < 0)
        {
            node = node.Next;
        }

        if (node is null)
        {
            _strokes.AddLast(stroke);
        }
        else
        {
            _strokes.AddBefore(node, stroke);
        }

        _ids.Add(stroke.Id);
    }

    private void FinishOpen(CanvasSession session)
    {
        if (session.Open is { } open && open.Epoch == _epoch && !open.Done)
        {
            open.Done = true;
            Broadcast(CanvasUpdate.ForStroke(_epoch, open.Id, open.Count, [], true), session.Subscriber);
        }

        session.Open = null;
    }

    private bool MakeRoom(int count)
    {
        var node = _strokes.First;
        while (_points + count > _maxPoints && node is not null)
        {
            var next = node.Next;
            if (node.Value.Done)
            {
                _points -= node.Value.Count;
                _ids.Remove(node.Value.Id);
                _strokes.Remove(node);
            }

            node = next;
        }

        return _points + count <= _maxPoints;
    }

    private void Broadcast(CanvasUpdate update, ICanvasSubscriber? except)
    {
        List<ICanvasSubscriber>? dropped = null;
        foreach (var subscriber in _subscribers)
        {
            if (!ReferenceEquals(subscriber, except) && !subscriber.Deliver(update))
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
