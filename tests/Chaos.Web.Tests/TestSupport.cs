using System.Buffers.Binary;
using System.Text;
using Chaos.Web.Drawing;
using Chaos.Web.Infrastructure;
using Chaos.Web.Latency;
using Chaos.Web.Messages;
using Chaos.Web.Postcards;
using Chaos.Web.Slots;
using Microsoft.Extensions.Options;

namespace Chaos.Web.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by)
    {
        _now += by;
        _timestamp += by.Ticks;
    }
}

internal static class Limits
{
    public static IOptions<ChaosOptions> Create(Action<ChaosOptions>? configure = null)
    {
        var options = new ChaosOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }
}

internal sealed class BoardRecorder(bool accept = true) : IBoardSubscriber
{
    public List<BoardUpdate> Updates { get; } = [];

    public bool Deliver(BoardUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal sealed class SlotRecorder(bool accept = true) : ISlotSubscriber
{
    public List<SlotUpdate> Updates { get; } = [];

    public bool Deliver(SlotUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal sealed class CanvasRecorder(bool accept = true) : ICanvasSubscriber
{
    public List<CanvasUpdate> Updates { get; } = [];

    public bool Deliver(CanvasUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal sealed class LatencyRecorder(bool accept = true) : ILatencySubscriber
{
    public List<LatencyUpdate> Updates { get; } = [];

    public bool Deliver(LatencyUpdate update)
    {
        if (accept)
        {
            lock (Updates)
            {
                Updates.Add(update);
            }
        }

        return accept;
    }
}

internal sealed class FakeProbe(Func<IReadOnlyList<LatencyEndpoint>, int?[]> measure) : ILatencyProbe
{
    public int Calls { get; private set; }

    public Task<int?[]> MeasureAsync(IReadOnlyList<LatencyEndpoint> endpoints, TimeSpan budget, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(measure(endpoints));
    }
}

internal sealed class PostcardRecorder(bool accept = true) : IPostcardSubscriber
{
    public List<PostcardUpdate> Updates { get; } = [];

    public bool Deliver(PostcardUpdate update)
    {
        if (accept)
        {
            Updates.Add(update);
        }

        return accept;
    }
}

internal static class TestImages
{
    public static byte[] Png(int width = 4, int height = 3, params string[] extra)
    {
        var bytes = new List<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(bytes, "IHDR", header);
        foreach (var name in extra)
        {
            Chunk(bytes, name, Encoding.ASCII.GetBytes("Comment\0secret"));
        }

        Chunk(bytes, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        Chunk(bytes, "IEND", []);
        return [.. bytes];
    }

    public static byte[] Gif(int width = 2, int height = 2, bool comment = false)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("GIF89a"));
        bytes.AddRange([(byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x80, 0, 0]);
        bytes.AddRange([0, 0, 0, 255, 255, 255]);
        bytes.AddRange([0x21, 0xFF, 11, .. Encoding.ASCII.GetBytes("NETSCAPE2.0"), 3, 1, 0, 0, 0]);
        if (comment)
        {
            bytes.AddRange([0x21, 0xFE, 6, .. Encoding.ASCII.GetBytes("secret"), 0]);
        }

        bytes.AddRange([0x21, 0xF9, 4, 0, 0, 0, 0, 0]);
        bytes.AddRange([0x2C, 0, 0, 0, 0, (byte)width, 0, (byte)height, 0, 0, 2, 2, 0x44, 0x01, 0]);
        bytes.Add(0x3B);
        return [.. bytes];
    }

    public static byte[] Jpeg(int width = 8, int height = 6, int orientation = 0, bool comment = false)
    {
        var bytes = new List<byte>([0xFF, 0xD8]);
        Segment(bytes, 0xE0, [.. Encoding.ASCII.GetBytes("JFIF\0"), 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        if (orientation > 0)
        {
            Segment(bytes, 0xE1,
            [
                .. Encoding.ASCII.GetBytes("Exif\0\0"),
                (byte)'I', (byte)'I', 0x2A, 0, 8, 0, 0, 0,
                2, 0,
                0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0,
                0x25, 0x88, 4, 0, 1, 0, 0, 0, 0x26, 0, 0, 0,
                0, 0, 0, 0,
            ]);
        }

        if (comment)
        {
            Segment(bytes, 0xFE, Encoding.ASCII.GetBytes("secret"));
        }

        Segment(bytes, 0xDB, new byte[65]);
        Segment(bytes, 0xC0, [8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);
        Segment(bytes, 0xDA, [1, 1, 0, 0, 63, 0]);
        bytes.AddRange([0x12, 0x34, 0x56, 0xFF, 0xD9]);
        return [.. bytes];
    }

    private static void Chunk(List<byte> bytes, string name, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        bytes.AddRange(length);
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        bytes.AddRange(data);
        bytes.AddRange([0, 0, 0, 0]);
    }

    private static void Segment(List<byte> bytes, byte marker, byte[] data)
    {
        bytes.AddRange([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
        bytes.AddRange(data);
    }
}
