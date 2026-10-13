using Chaos.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Drawing;

internal sealed class CanvasService
{
    public const int MaxCoordinate = 4095;
    public const int MaxBatchPoints = 256;
    public const int MaxStrokePoints = 2048;

    public static readonly TimeSpan ResetCooldown = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly int _maxPoints;
    private readonly double _pointBurst;
    private readonly double _pointsPerSecond;
    private readonly KeyedTokenBuckets _resets;
    private readonly Lock _gate = new();
    private readonly LinkedList<Stroke> _strokes = new();
    private readonly HashSet<ICanvasSubscriber> _subscribers = [];
    private int _epoch = 1;
    private long _nextStroke;
    private int _points;
    private long? _lastReset;

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
    }

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

        if (points.Length % 2 != 0 || points.Length > MaxBatchPoints * 2 || (points.Length == 0 && !end))
        {
            return DrawResult.Invalid;
        }

        foreach (var value in points)
        {
            if ((uint)value > MaxCoordinate)
            {
                return DrawResult.Invalid;
            }
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
                stroke = new Stroke(++_nextStroke, _epoch, clientStroke);
                _strokes.AddLast(stroke);
                session.Open = stroke;
                session.LastClientId = clientStroke;
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
            _strokes.Clear();
            _points = 0;
            Broadcast(CanvasUpdate.ForReset(_epoch), null);
            return ResetResult.Accepted;
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
            var strokes = _strokes.Select(stroke => new StrokeSnapshot(stroke.Id, stroke.ToArray(), stroke.Done)).ToArray();
            if (subscriber.Deliver(CanvasUpdate.ForSnapshot(_epoch, strokes)))
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
