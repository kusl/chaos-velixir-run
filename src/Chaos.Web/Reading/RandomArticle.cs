namespace Chaos.Web.Reading;

internal static class RandomArticle
{
    public const string Path = "/read";

    public static IApplicationBuilder UseRandomArticle(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(static (context, next) =>
        {
            var request = context.Request;
            if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                || !string.Equals(request.Path.Value?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase)
                || context.RequestServices.GetRequiredService<ArticleLibrary>().Random() is not { } article)
            {
                return next(context);
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Redirect($"{request.PathBase}{Path}/{article.Id}");
            return Task.CompletedTask;
        });
    }
}
