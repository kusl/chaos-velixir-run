using System.Threading.Channels;

namespace Chaos.Web.Infrastructure;

internal sealed class Outbox<T> : IAsyncDisposable
{
    private readonly Channel<T> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<IReadOnlyList<T>, CancellationToken, Task> _send;
    private readonly int _maxBatch;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly Task _pump;
    private volatile bool _faulted;

    public Outbox(int capacity, int maxBatch, TimeSpan timeout, Func<IReadOnlyList<T>, CancellationToken, Task> send, ILogger logger)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _maxBatch = maxBatch;
        _timeout = timeout;
        _send = send;
        _logger = logger;
        _pump = Task.Run(PumpAsync);
    }

    public bool IsFaulted => _faulted;

    public bool TryPost(T item) => !_faulted && _channel.Writer.TryWrite(item);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task PumpAsync()
    {
        var batch = new List<T>(_maxBatch);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (batch.Count < _maxBatch && _channel.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(_timeout);
                await _send([.. batch], timeout.Token).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _faulted = true;
            _channel.Writer.TryComplete();
            OutboxLog.SendFailed(_logger, exception);
        }
    }
}

internal static partial class OutboxLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Realtime delivery stopped")]
    public static partial void SendFailed(ILogger logger, Exception exception);
}
