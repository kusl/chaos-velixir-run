using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Chaos.Web.Latency;

internal sealed class HttpLatencyProbe : ILatencyProbe, IDisposable
{
    public const int Samples = 3;
    public const int Parallelism = 6;
    public const string UserAgent = "chaos-velixir-run/1.0 (+https://github.com/kusl/chaos-velixir-run)";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _client;

    public HttpLatencyProbe()
    {
        _handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = RequestTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 2,
        };
        _client = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<int?[]> MeasureAsync(IReadOnlyList<LatencyEndpoint> endpoints, TimeSpan budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var results = new int?[endpoints.Count];
        var targets = new List<(int Index, Uri Uri)>();
        for (var i = 0; i < endpoints.Count; i++)
        {
            if (endpoints[i].FromServer && Uri.TryCreate(endpoints[i].Url, UriKind.Absolute, out var uri))
            {
                targets.Add((i, uri));
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = deadline.Token };
        try
        {
            await Parallel.ForEachAsync(targets, options, async (target, token) =>
            {
                results[target.Index] = await MeasureOneAsync(target.Uri, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
        }

        return results;
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    private async Task<int?> MeasureOneAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (await TimeAsync(uri, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        int? best = null;
        for (var i = 0; i < Samples; i++)
        {
            if (await TimeAsync(uri, cancellationToken).ConfigureAwait(false) is not { } elapsed)
            {
                break;
            }

            best = best is { } current ? Math.Min(current, elapsed) : elapsed;
        }

        return best;
    }

    private async Task<int?> TimeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            var start = Stopwatch.GetTimestamp();
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(start);
            await response.Content.CopyToAsync(Stream.Null, timeout.Token).ConfigureAwait(false);
            return (int)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }
}
