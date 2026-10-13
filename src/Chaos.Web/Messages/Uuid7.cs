namespace Chaos.Web.Messages;

internal static class Uuid7
{
    public static bool IsValid(Guid id) => id.Version == 7 && (id.Variant & 0b1100) == 0b1000;

    public static long UnixMilliseconds(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return milliseconds;
    }

    public static DateTimeOffset Timestamp(Guid id) => DateTimeOffset.FromUnixTimeMilliseconds(UnixMilliseconds(id));
}
