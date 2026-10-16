namespace Chaos.Web.Infrastructure;

internal sealed class KeyedCooldowns(TimeSpan cooldown, TimeProvider time, int maxKeys = 10_000)
{
    private readonly Dictionary<string, long> _stamps = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public TimeSpan Remaining(string key)
    {
        lock (_gate)
        {
            return RemainingAt(key, time.GetTimestamp());
        }
    }

    public bool TryStart(string key, out TimeSpan remaining)
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            remaining = RemainingAt(key, now);
            if (remaining > TimeSpan.Zero)
            {
                return false;
            }

            if (_stamps.Count >= maxKeys && !_stamps.ContainsKey(key))
            {
                Sweep(now);
            }

            _stamps[key] = now;
            return true;
        }
    }

    private TimeSpan RemainingAt(string key, long now)
    {
        if (!_stamps.TryGetValue(key, out var stamp))
        {
            return TimeSpan.Zero;
        }

        var left = cooldown - time.GetElapsedTime(stamp, now);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void Sweep(long now)
    {
        foreach (var (key, stamp) in _stamps)
        {
            if (time.GetElapsedTime(stamp, now) >= cooldown)
            {
                _stamps.Remove(key);
            }
        }

        if (_stamps.Count >= maxKeys)
        {
            _stamps.Clear();
        }
    }
}
