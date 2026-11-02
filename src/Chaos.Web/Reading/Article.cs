namespace Chaos.Web.Reading;

internal sealed record Article(
    string Id,
    string Title,
    string? Dek,
    string? Byline,
    DateTimeOffset? Published,
    Uri? Source,
    string? Summary,
    IReadOnlyList<ArticleBlock> Blocks);
