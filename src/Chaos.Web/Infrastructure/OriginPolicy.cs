namespace Chaos.Web.Infrastructure;

internal static class OriginPolicy
{
    public static bool IsAllowed(string? origin, string? host, IEnumerable<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(host) && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = uri.GetLeftPart(UriPartial.Authority);
        return allowed.Any(entry => string.Equals(entry.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
