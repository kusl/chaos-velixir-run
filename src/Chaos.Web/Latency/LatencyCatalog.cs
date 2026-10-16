namespace Chaos.Web.Latency;

internal static class LatencyCatalog
{
    public static IReadOnlyList<LatencyEndpoint> Endpoints { get; } =
    [
        new("chaos", "This site", "/time", FromServer: false),
        new("cloudflare", "Cloudflare", "https://www.cloudflare.com/cdn-cgi/trace"),
        new("one-one", "Cloudflare 1.1.1.1", "https://one.one.one.one/cdn-cgi/trace"),
        new("google", "Google", "https://www.google.com/generate_204"),
        new("gstatic", "Google Static", "https://www.gstatic.com/generate_204"),
        new("dns-google", "Google Public DNS", "https://dns.google/resolve?name=example.com&type=A"),
        new("apple", "Apple", "https://www.apple.com/library/test/success.html"),
        new("microsoft", "Microsoft", "https://www.microsoft.com/favicon.ico"),
        new("firefox", "Mozilla", "https://detectportal.firefox.com/success.txt"),
        new("github", "GitHub", "https://github.com/favicon.ico"),
        new("wikipedia", "Wikipedia", "https://www.wikipedia.org/static/favicon/wikipedia.ico"),
        new("example", "example.com", "https://example.com/"),
        new("fastly", "Fastly", "https://www.fastly.com/robots.txt"),
        new("akamai", "Akamai", "https://www.akamai.com/robots.txt"),
        new("jsdelivr", "jsDelivr", "https://cdn.jsdelivr.net/robots.txt"),
        new("npm", "npm registry", "https://registry.npmjs.org/-/ping"),
        new("nuget", "NuGet", "https://api.nuget.org/v3/index.json"),
        new("pypi", "PyPI", "https://pypi.org/robots.txt"),
        new("debian", "Debian", "https://deb.debian.org/debian/README"),
        new("kernel", "kernel.org", "https://www.kernel.org/robots.txt"),
        new("aws-us-east-1", "AWS N. Virginia", "https://dynamodb.us-east-1.amazonaws.com/ping"),
        new("aws-us-east-2", "AWS Ohio", "https://dynamodb.us-east-2.amazonaws.com/ping"),
        new("aws-us-west-1", "AWS N. California", "https://dynamodb.us-west-1.amazonaws.com/ping"),
        new("aws-us-west-2", "AWS Oregon", "https://dynamodb.us-west-2.amazonaws.com/ping"),
        new("aws-ca-central-1", "AWS Canada", "https://dynamodb.ca-central-1.amazonaws.com/ping"),
        new("aws-sa-east-1", "AWS São Paulo", "https://dynamodb.sa-east-1.amazonaws.com/ping"),
        new("aws-eu-west-1", "AWS Ireland", "https://dynamodb.eu-west-1.amazonaws.com/ping"),
        new("aws-eu-west-2", "AWS London", "https://dynamodb.eu-west-2.amazonaws.com/ping"),
        new("aws-eu-central-1", "AWS Frankfurt", "https://dynamodb.eu-central-1.amazonaws.com/ping"),
        new("aws-eu-north-1", "AWS Stockholm", "https://dynamodb.eu-north-1.amazonaws.com/ping"),
        new("aws-me-south-1", "AWS Bahrain", "https://dynamodb.me-south-1.amazonaws.com/ping"),
        new("aws-af-south-1", "AWS Cape Town", "https://dynamodb.af-south-1.amazonaws.com/ping"),
        new("aws-ap-south-1", "AWS Mumbai", "https://dynamodb.ap-south-1.amazonaws.com/ping"),
        new("aws-ap-southeast-1", "AWS Singapore", "https://dynamodb.ap-southeast-1.amazonaws.com/ping"),
        new("aws-ap-southeast-2", "AWS Sydney", "https://dynamodb.ap-southeast-2.amazonaws.com/ping"),
        new("aws-ap-northeast-1", "AWS Tokyo", "https://dynamodb.ap-northeast-1.amazonaws.com/ping"),
        new("aws-ap-northeast-2", "AWS Seoul", "https://dynamodb.ap-northeast-2.amazonaws.com/ping"),
    ];

    public static IReadOnlyList<string> Origins { get; } =
        [.. Endpoints.Select(endpoint => endpoint.Origin).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

    public static string LabelOf(string key) =>
        Endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Key, key, StringComparison.Ordinal))?.Label ?? key;
}
