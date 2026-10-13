using Microsoft.Extensions.Options;

namespace Chaos.Web.Infrastructure;

internal static partial class HubGuard
{
    public static readonly PathString HubPath = new("/_blazor");

    public static IApplicationBuilder UseHubGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var tracker = app.ApplicationServices.GetRequiredService<ConnectionTracker>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<ChaosOptions>>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Chaos.Web.HubGuard");

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(HubPath, out var remainder))
            {
                await next(context);
                return;
            }

            if (!OriginPolicy.IsAllowed(context.Request.Headers.Origin, context.Request.Host.Value, options.Value.AllowedOrigins))
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Origin not allowed.");
                return;
            }

            if (remainder.HasValue)
            {
                await next(context);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                await RejectAsync(context, StatusCodes.Status400BadRequest, "Only WebSocket connections are accepted.");
                return;
            }

            var admission = tracker.TryAdmit(ClientAddress.Key(context.Connection.RemoteIpAddress), out var lease);
            if (admission is not ConnectionAdmission.Admitted)
            {
                if (admission is ConnectionAdmission.ServerFull)
                {
                    LogServerFull(logger, tracker.Total);
                }

                context.Response.Headers.RetryAfter = "10";
                var status = admission is ConnectionAdmission.ServerFull
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status429TooManyRequests;
                await RejectAsync(context, status, "Too many connections.");
                return;
            }

            using (lease)
            {
                await next(context);
            }
        });
    }

    private static Task RejectAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, context.RequestAborted);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a connection while holding {Connections} connections")]
    private static partial void LogServerFull(ILogger logger, int connections);
}
