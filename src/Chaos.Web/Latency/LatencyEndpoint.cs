namespace Chaos.Web.Latency;

internal sealed record LatencyEndpoint(string Key, string Label, string Url, bool FromServer = true)
{
    public Uri? Remote =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    public string? Origin => Remote?.GetLeftPart(UriPartial.Authority);
}
