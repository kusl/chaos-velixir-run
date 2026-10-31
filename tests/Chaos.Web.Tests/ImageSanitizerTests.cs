using System.Text;
using Chaos.Web.Postcards;

namespace Chaos.Web.Tests;

public sealed class ImageSanitizerTests
{
    [Fact]
    public void AcceptsPngAndReadsItsSize()
    {
        var image = Assert.IsType<CleanImage>(ImageSanitizer.Clean(TestImages.Png(40, 30)));
        Assert.Equal(new PostcardImage("image/png", 40, 30, image.Bytes.Length), image.Info);
    }

    [Fact]
    public void DropsPngTextAndExifChunks()
    {
        var original = TestImages.Png(4, 3, "tEXt", "eXIf", "zTXt");
        var image = Assert.IsType<CleanImage>(ImageSanitizer.Clean(original));
        Assert.Equal(TestImages.Png(4, 3), image.Bytes);
        Assert.DoesNotContain("secret", Encoding.ASCII.GetString(image.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPngWithUnknownCriticalChunks()
    {
        Assert.Null(ImageSanitizer.Clean(TestImages.Png(4, 3, "ZZZZ")));
    }

    [Fact]
    public void AcceptsGifAndDropsComments()
    {
        var image = Assert.IsType<CleanImage>(ImageSanitizer.Clean(TestImages.Gif(2, 2, comment: true)));
        Assert.Equal(new PostcardImage("image/gif", 2, 2, image.Bytes.Length), image.Info);
        Assert.Equal(TestImages.Gif(2, 2), image.Bytes);
    }

    [Fact]
    public void AcceptsJpegAndKeepsOnlyTheOrientation()
    {
        var image = Assert.IsType<CleanImage>(ImageSanitizer.Clean(TestImages.Jpeg(8, 6, orientation: 6, comment: true)));
        Assert.Equal(new PostcardImage("image/jpeg", 8, 6, image.Bytes.Length), image.Info);
        var text = Encoding.ASCII.GetString(image.Bytes);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("Exif", text, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(image.Bytes, [0xFF, 0xE1]));
        Assert.Equal(-1, image.Bytes.AsSpan().IndexOf([(byte)0x25, (byte)0x88]));
        Assert.True(image.Bytes.AsSpan().IndexOf([(byte)0x01, (byte)0x12, (byte)0x00, (byte)0x03]) > 0);
        Assert.Equal([0xFF, 0xD9], image.Bytes[^2..]);
    }

    [Fact]
    public void JpegWithoutOrientationLosesItsExif()
    {
        var image = Assert.IsType<CleanImage>(ImageSanitizer.Clean(TestImages.Jpeg(8, 6, orientation: 1)));
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(image.Bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(PostcardRules.MaxSide + 1, 10)]
    [InlineData(10, PostcardRules.MaxSide + 1)]
    public void RejectsImpossibleSizes(int width, int height)
    {
        Assert.Null(ImageSanitizer.Clean(TestImages.Png(width, height)));
        Assert.Null(ImageSanitizer.Clean(TestImages.Jpeg(width, height)));
    }

    [Fact]
    public void RejectsOtherFormatsTruncationAndOversize()
    {
        Assert.Null(ImageSanitizer.Clean([]));
        Assert.Null(ImageSanitizer.Clean("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8));
        Assert.Null(ImageSanitizer.Clean("RIFF0000WEBPVP8 "u8));
        var png = TestImages.Png();
        Assert.Null(ImageSanitizer.Clean(png.AsSpan(0, png.Length - 6)));
        var gif = TestImages.Gif();
        Assert.Null(ImageSanitizer.Clean(gif.AsSpan(0, gif.Length - 1)));
        var jpeg = TestImages.Jpeg();
        Assert.Null(ImageSanitizer.Clean(jpeg.AsSpan(0, 30)));
        var huge = new byte[PostcardRules.MaxImageBytes + 1];
        png.CopyTo(huge, 0);
        Assert.Null(ImageSanitizer.Clean(huge));
    }

    private static int CountOf(byte[] bytes, byte[] pattern)
    {
        var count = 0;
        var span = bytes.AsSpan();
        int index;
        while ((index = span.IndexOf(pattern)) >= 0)
        {
            count++;
            span = span[(index + pattern.Length)..];
        }

        return count;
    }
}
