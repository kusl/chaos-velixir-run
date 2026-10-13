namespace Chaos.Web.Infrastructure;

internal sealed class KeyedTokenBuckets(double capacity, double perSecond, TimeProvider time, int maxKeys = 10_000)
{
    private readonly Dictionary<string, TokenBucket> _buckets = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _buckets.Count;
            }
        }
    }

    public bool TryTake(string key, double count = 1)
    {
        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                if (_buckets.Count >= maxKeys)
                {
                    Sweep();
                }

                bucket = new TokenBucket(capacity, perSecond, time);
                _buckets[key] = bucket;
            }

            return bucket.TryTake(count);
        }
    }

    private void Sweep()
    {
        foreach (var (key, bucket) in _buckets)
        {
            if (bucket.IsFull)
            {
                _buckets.Remove(key);
            }
        }

        if (_buckets.Count >= maxKeys)
        {
            _buckets.Clear();
        }
    }
}
