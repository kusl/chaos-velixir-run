namespace Chaos.Web.Postcards;

internal static class PostcardEndpoint
{
    public const string Prefix = "postcard/images";

    public static string PathOf(Guid id, int index) => $"{Prefix}/{id:D}/{index}";

    public static IEndpointRouteBuilder MapPostcardImages(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet($"/{Prefix}/{{id:guid}}/{{index:int}}", static (Guid id, int index, PostcardService postcards, HttpResponse response) =>
        {
            if (!postcards.TryGetImage(id, index, out var image, out var bytes) || image is null)
            {
                response.Headers.CacheControl = "no-store";
                return Results.NotFound();
            }

            response.Headers.CacheControl = "private, max-age=31536000, immutable";
            response.Headers.ContentDisposition = $"inline; filename=\"postcard-{index + 1}.{Extension(image.Type)}\"";
            return Results.Bytes(bytes, image.Type);
        });
        return app;
    }

    private static string Extension(string type) => type switch
    {
        ImageSanitizer.Png => "png",
        ImageSanitizer.Gif => "gif",
        _ => "jpg",
    };
}
