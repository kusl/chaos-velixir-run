using System.Buffers.Binary;

namespace Chaos.Web.Postcards;

internal static class ImageSanitizer
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string Gif = "image/gif";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> PngKept = new(StringComparer.Ordinal)
    {
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "iCCP", "sBIT", "sRGB", "cICP", "mDCV", "cLLI", "pHYs", "bKGD", "hIST", "acTL", "fcTL", "fdAT",
    };

    public static CleanImage? Clean(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > PostcardRules.MaxImageBytes)
        {
            return null;
        }

        if (data.StartsWith(PngSignature))
        {
            return CleanPng(data);
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return CleanJpeg(data);
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return CleanGif(data);
        }

        return null;
    }

    private static CleanImage? Finish(string type, int width, int height, MemoryStream output)
    {
        if (!PostcardRules.FitsSize(width, height))
        {
            return null;
        }

        var bytes = output.ToArray();
        return new CleanImage(new PostcardImage(type, width, height, bytes.Length), bytes);
    }

    private static CleanImage? CleanPng(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);
        var position = PngSignature.Length;
        var width = 0;
        var height = 0;
        var first = true;
        var hasData = false;
        while (true)
        {
            if (data.Length - position < 12)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            if (length > (uint)(data.Length - position - 12))
            {
                return null;
            }

            var type = data.Slice(position + 4, 4);
            if (!IsChunkType(type))
            {
                return null;
            }

            var name = System.Text.Encoding.ASCII.GetString(type);
            var chunk = data.Slice(position, (int)length + 12);
            if (first)
            {
                if (name != "IHDR" || length != 13)
                {
                    return null;
                }

                width = ToInt(BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8)..]));
                height = ToInt(BinaryPrimitives.ReadUInt32BigEndian(data[(position + 12)..]));
                first = false;
            }
            else if (name == "IHDR")
            {
                return null;
            }

            var critical = (type[0] & 0x20) == 0;
            if (PngKept.Contains(name))
            {
                output.Write(chunk);
                hasData |= name == "IDAT";
            }
            else if (critical)
            {
                return null;
            }

            position += chunk.Length;
            if (name == "IEND")
            {
                return hasData ? Finish(Png, width, height, output) : null;
            }
        }
    }

    private static bool IsChunkType(ReadOnlySpan<byte> type)
    {
        foreach (var b in type)
        {
            if (!char.IsAsciiLetter((char)b))
            {
                return false;
            }
        }

        return true;
    }

    private static int ToInt(uint value) => value > int.MaxValue ? 0 : (int)value;

    private static CleanImage? CleanJpeg(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write([0xFF, 0xD8]);
        var position = 2;
        var width = 0;
        var height = 0;
        var orientation = 0;
        var sawFrame = false;
        var wroteOrientation = false;
        while (true)
        {
            if (position >= data.Length || data[position] != 0xFF)
            {
                return null;
            }

            while (position < data.Length && data[position] == 0xFF)
            {
                position++;
            }

            if (position >= data.Length)
            {
                return null;
            }

            var marker = data[position];
            position++;
            if (marker is 0xD8 or 0xD9 or 0x00)
            {
                return null;
            }

            if (marker is (>= 0xD0 and <= 0xD7) or 0x01)
            {
                output.Write([0xFF, marker]);
                continue;
            }

            if (data.Length - position < 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (length < 2 || length > data.Length - position)
            {
                return null;
            }

            var body = data.Slice(position + 2, length - 2);
            if (marker == 0xDA)
            {
                if (!sawFrame)
                {
                    return null;
                }

                if (orientation is > 1 and <= 8 && !wroteOrientation)
                {
                    WriteOrientation(output, orientation);
                }

                output.Write([0xFF, marker]);
                output.Write(data[position..]);
                return Finish(Jpeg, width, height, output);
            }

            if (IsFrame(marker))
            {
                if (body.Length < 6 || sawFrame)
                {
                    return null;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(body[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(body[3..]);
                sawFrame = true;
            }

            if (marker == 0xE1 && orientation == 0)
            {
                orientation = ReadOrientation(body);
            }

            if (KeepsJpegSegment(marker, body))
            {
                if (marker != 0xE0 && orientation is > 1 and <= 8 && !wroteOrientation)
                {
                    WriteOrientation(output, orientation);
                    wroteOrientation = true;
                }

                output.Write([0xFF, marker]);
                output.Write(data.Slice(position, length));
            }

            position += length;
        }
    }

    private static bool IsFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    private static bool KeepsJpegSegment(byte marker, ReadOnlySpan<byte> body) => marker switch
    {
        0xE0 => true,
        0xE2 => body.StartsWith("ICC_PROFILE\0"u8),
        0xEE => body.StartsWith("Adobe"u8),
        >= 0xE1 and <= 0xEF => false,
        0xFE => false,
        _ => true,
    };

    private static int ReadOrientation(ReadOnlySpan<byte> body)
    {
        if (!body.StartsWith("Exif\0\0"u8))
        {
            return 0;
        }

        var tiff = body[6..];
        if (tiff.Length < 8)
        {
            return 0;
        }

        bool little;
        if (tiff.StartsWith("II*\0"u8))
        {
            little = true;
        }
        else if (tiff.StartsWith("MM\0*"u8))
        {
            little = false;
        }
        else
        {
            return 0;
        }

        var ifd = ReadUInt32(tiff[4..], little);
        if (ifd > (uint)(tiff.Length - 2))
        {
            return 0;
        }

        var count = ReadUInt16(tiff[(int)ifd..], little);
        var entries = (int)ifd + 2;
        for (var i = 0; i < count; i++)
        {
            var at = entries + (i * 12);
            if (at + 12 > tiff.Length)
            {
                return 0;
            }

            if (ReadUInt16(tiff[at..], little) == 0x0112 && ReadUInt16(tiff[(at + 2)..], little) == 3)
            {
                var value = ReadUInt16(tiff[(at + 8)..], little);
                return value is >= 1 and <= 8 ? value : 0;
            }
        }

        return 0;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

    private static void WriteOrientation(MemoryStream output, int orientation)
    {
        output.Write(
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ]);
    }

    private static CleanImage? CleanGif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var packed = data[10];
        var position = 13;
        if ((packed & 0x80) != 0)
        {
            position += 3 * (1 << ((packed & 0x07) + 1));
        }

        if (position > data.Length)
        {
            return null;
        }

        using var output = new MemoryStream(data.Length);
        output.Write(data[..position]);
        var frames = 0;
        while (position < data.Length)
        {
            var start = position;
            var block = data[position];
            switch (block)
            {
                case 0x3B:
                    output.WriteByte(0x3B);
                    return frames > 0 ? Finish(Gif, width, height, output) : null;
                case 0x21:
                {
                    if (data.Length - position < 2)
                    {
                        return null;
                    }

                    var label = data[position + 1];
                    var end = SkipSubBlocks(data, position + 2);
                    if (end < 0)
                    {
                        return null;
                    }

                    if (KeepsGifExtension(label, data[(position + 2)..end]))
                    {
                        output.Write(data[start..end]);
                    }

                    position = end;
                    break;
                }

                case 0x2C:
                {
                    if (data.Length - position < 11 || ++frames > PostcardRules.MaxFrames)
                    {
                        return null;
                    }

                    var local = data[position + 9];
                    var after = position + 10;
                    if ((local & 0x80) != 0)
                    {
                        after += 3 * (1 << ((local & 0x07) + 1));
                    }

                    if (after >= data.Length)
                    {
                        return null;
                    }

                    var end = SkipSubBlocks(data, after + 1);
                    if (end < 0)
                    {
                        return null;
                    }

                    output.Write(data[start..end]);
                    position = end;
                    break;
                }

                default:
                    return null;
            }
        }

        return null;
    }

    private static int SkipSubBlocks(ReadOnlySpan<byte> data, int position)
    {
        while (position < data.Length)
        {
            var size = data[position];
            position += 1 + size;
            if (size == 0)
            {
                return position <= data.Length ? position : -1;
            }
        }

        return -1;
    }

    private static bool KeepsGifExtension(byte label, ReadOnlySpan<byte> blocks) => label switch
    {
        0xF9 => true,
        0xFF => blocks.Length >= 12 && blocks[0] == 11 && (blocks[1..12].SequenceEqual("NETSCAPE2.0"u8) || blocks[1..12].SequenceEqual("ANIMEXTS1.0"u8)),
        _ => false,
    };
}
