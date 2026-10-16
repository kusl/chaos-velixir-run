namespace Chaos.Web.Latency;

internal sealed record LatencyEndpoint(string Key, string Label, string Url, bool FromServer = true)
{
    public string? Origin => Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
}
