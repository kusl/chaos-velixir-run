using System.Globalization;
using System.Text.Json.Serialization;

namespace Chaos.Web.Infrastructure;

internal sealed record ServerTime(long UnixMs, int OffsetMinutes)
{
    public static ServerTime Now(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        var local = time.GetLocalNow();
        return new ServerTime(local.ToUnixTimeMilliseconds(), (int)Math.Round(local.Offset.TotalMinutes));
    }

    [JsonIgnore]
    public string Iso => DateTimeOffset.FromUnixTimeMilliseconds(UnixMs).ToOffset(TimeSpan.FromMinutes(OffsetMinutes))
        .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    [JsonIgnore]
    public string Label => DateTimeOffset.FromUnixTimeMilliseconds(UnixMs).ToOffset(TimeSpan.FromMinutes(OffsetMinutes))
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Zone(OffsetMinutes);

    public static string Zone(int offsetMinutes)
    {
        if (offsetMinutes == 0)
        {
            return "UTC";
        }

        var sign = offsetMinutes < 0 ? "-" : "+";
        var magnitude = Math.Abs(offsetMinutes);
        return string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{magnitude / 60:00}:{magnitude % 60:00}");
    }
}
