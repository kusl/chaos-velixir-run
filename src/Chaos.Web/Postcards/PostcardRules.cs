namespace Chaos.Web.Postcards;

internal static class PostcardRules
{
    public const int MaxTextLength = 5000;
    public const int MaxImages = 4;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxTotalBytes = 4 * 1024 * 1024;
    public const int MaxSide = 4096;
    public const long MaxPixels = (long)MaxSide * MaxSide;
    public const int MaxFrames = 1000;

    public static bool FitsSize(int width, int height) =>
        width is > 0 and <= MaxSide && height is > 0 and <= MaxSide && (long)width * height <= MaxPixels;
}
