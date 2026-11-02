namespace Chaos.Web.Tests;

internal static class ArticleSamples
{
    public static readonly string Rule = new('=', 80);

    public const string FirstUrl = "https://www.nytimes.com/2026/10/05/science/consciousness-brain-babies.html";

    public const string SecondUrl = "https://www.nytimes.com/2024/01/04/well/live/live-longer-health.html";

    public static string First => Record(
        "consciousness-brain-babies.html",
        "When does it begin?",
        """
        Are Babies Conscious?
        By pinpointing when it begins in young humans, scientists hope to better understand what consciousness is.
        By Carl Zimmer
        Published 2026-10-05 09:00 UTC
        https://www.nytimes.com/2026/10/05/science/consciousness-brain-babies.html
        """,
        """
        A newborn’s brain is busy.

        Researchers listed three signs:
        """,
        """
        - Attention to faces.
        - Surprise at the unexpected.
        • A sense of time.
        """,
        "https://example.com/" + new string('a', 300));

    public static string Second => Record(
        "live-longer-health.html",
        "Move more.",
        """
        The 7 Keys to Longevity
        Ignore the hyperbaric chambers and infrared light.
        By Dana G. Smith
        Published 2024-01-04 10:00 UTC
        https://www.nytimes.com/2024/01/04/well/live/live-longer-health.html
        """,
        "Humans have searched for the secret to immortality for thousands of years.",
        "1. Move more.");

    public static string Both => First + Second;

    public static string Record(string slug, string summary, string head, params string[] paragraphs) =>
        $"{Rule}\n{slug}\nSource: PreloadedData  Paragraphs: {paragraphs.Length}\n{Rule}\n\nSUMMARY\n\n{summary}\n\nARTICLE\n\n{head.Trim()}\n\n{string.Join("\n\n", paragraphs.Select(paragraph => paragraph.Trim()))}\n\n";
}
