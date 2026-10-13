namespace Chaos.Web.Infrastructure;

internal static class ModulePath
{
    public static string Of(string asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.StartsWith('/') || asset.StartsWith("./", StringComparison.Ordinal) ? asset : "./" + asset;
    }
}
