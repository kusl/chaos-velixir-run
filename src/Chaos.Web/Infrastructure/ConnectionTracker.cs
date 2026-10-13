using Microsoft.Extensions.Options;

namespace Chaos.Web.Infrastructure;

internal enum ConnectionAdmission
{
    Admitted,
    ClientFull,
    ServerFull,
}

internal sealed class ConnectionTracker(IOptions<ChaosOptions> options)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _perClient = new(StringComparer.Ordinal);
    private int _total;

    public int Total
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    public int CountFor(string client)
    {
        lock (_gate)
        {
            return _perClient.GetValueOrDefault(client);
        }
    }

    public ConnectionAdmission TryAdmit(string client, out IDisposable? lease)
    {
        var limits = options.Value;
        lock (_gate)
        {
            if (_total >= limits.MaxConnections)
            {
                lease = null;
                return ConnectionAdmission.ServerFull;
            }

            var count = _perClient.GetValueOrDefault(client);
            if (count >= limits.MaxConnectionsPerClient)
            {
                lease = null;
                return ConnectionAdmission.ClientFull;
            }

            _perClient[client] = count + 1;
            _total++;
        }

        lease = new Lease(this, client);
        return ConnectionAdmission.Admitted;
    }

    private void Release(string client)
    {
        lock (_gate)
        {
            var count = _perClient.GetValueOrDefault(client) - 1;
            if (count > 0)
            {
                _perClient[client] = count;
            }
            else
            {
                _perClient.Remove(client);
            }

            _total--;
        }
    }

    private sealed class Lease(ConnectionTracker owner, string client) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(client);
            }
        }
    }
}
