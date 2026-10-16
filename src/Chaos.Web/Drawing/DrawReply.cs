using System.Globalization;

namespace Chaos.Web.Drawing;

internal static class DrawReply
{
    public static string Format(DrawResult result, Guid stroke)
    {
        var code = ((int)result).ToString(CultureInfo.InvariantCulture);
        return result is DrawResult.Accepted && stroke != Guid.Empty ? string.Create(CultureInfo.InvariantCulture, $"{code} {stroke:D}") : code;
    }
}
