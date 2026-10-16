namespace Chaos.Web.Latency;

internal interface ILatencyProbe
{
    Task<int?[]> MeasureAsync(IReadOnlyList<LatencyEndpoint> endpoints, TimeSpan budget, CancellationToken cancellationToken);
}
