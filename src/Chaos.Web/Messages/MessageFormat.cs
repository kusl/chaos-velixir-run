using System.Globalization;

namespace Chaos.Web.Messages;

internal static class MessageFormat
{
    public static string Iso(Guid id) =>
        Uuid7.Timestamp(id).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string Label(Guid id, DateTimeOffset now)
    {
        var at = Uuid7.Timestamp(id).UtcDateTime;
        return at.Date == now.UtcDateTime.Date
            ? at.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
