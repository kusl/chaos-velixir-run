using Microsoft.AspNetCore.Antiforgery;

namespace Chaos.Web.Infrastructure;

internal sealed class CookielessAntiforgery : IAntiforgery
{
    private static readonly AntiforgeryTokenSet NoTokens = new(null, null, "__RequestVerificationToken", null);

    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => NoTokens;

    public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => NoTokens;

    public Task<bool> IsRequestValidAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return Task.FromResult(IsSafe(httpContext.Request.Method));
    }

    public Task ValidateRequestAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return IsSafe(httpContext.Request.Method)
            ? Task.CompletedTask
            : Task.FromException(new AntiforgeryValidationException("This site accepts no form posts."));
    }

    public void SetCookieTokenAndHeader(HttpContext httpContext)
    {
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
}
