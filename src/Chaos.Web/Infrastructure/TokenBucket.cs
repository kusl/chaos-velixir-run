namespace Chaos.Web.Infrastructure;

internal sealed class TokenBucket
{
    private readonly double _capacity;
    private readonly double _perSecond;
    private readonly TimeProvider _time;
    private double _tokens;
    private long _stamp;

    public TokenBucket(double capacity, double perSecond, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _capacity = capacity;
        _perSecond = perSecond;
        _time = time;
        _tokens = capacity;
        _stamp = time.GetTimestamp();
    }

    public bool IsFull
    {
        get
        {
            Refill();
            return _tokens >= _capacity;
        }
    }

    public bool TryTake(double count = 1)
    {
        Refill();
        if (_tokens < count)
        {
            return false;
        }

        _tokens -= count;
        return true;
    }

    private void Refill()
    {
        var now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(_stamp, now).TotalSeconds;
        _stamp = now;
        _tokens = Math.Min(_capacity, _tokens + (elapsed * _perSecond));
    }
}
