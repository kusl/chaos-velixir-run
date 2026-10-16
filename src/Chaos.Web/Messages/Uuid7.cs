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

    public static Guid After(Guid previous, DateTimeOffset now)
    {
        var candidate = Guid.CreateVersion7(now);
        if (candidate.CompareTo(previous) > 0 || !IsValid(previous))
        {
            return candidate;
        }

        Span<byte> bytes = stackalloc byte[16];
        previous.TryWriteBytes(bytes, bigEndian: true, out _);
        for (var i = 15; i >= 9; i--)
        {
            if (++bytes[i] != 0)
            {
                return new Guid(bytes, bigEndian: true);
            }
        }

        if ((bytes[8] & 0b0011_1111) != 0b0011_1111)
        {
            bytes[8]++;
            return new Guid(bytes, bigEndian: true);
        }

        return candidate;
    }
}
