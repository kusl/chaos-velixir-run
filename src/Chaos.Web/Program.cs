using System.IO.Compression;
using Chaos.Web.Components;
using Chaos.Web.Drawing;
using Chaos.Web.Infrastructure;
using Chaos.Web.Messages;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

var publishedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(publishedWebRoot) ? publishedWebRoot : null,
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port))
    {
        kestrel.ListenAnyIP(port);
    }
});

builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(10));
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
});

builder.Services.AddOptions<ChaosOptions>()
    .Bind(builder.Configuration.GetSection(ChaosOptions.SectionName))
    .Validate(chaos => chaos.IsValid(), "Chaos limits are out of range.")
    .ValidateOnStart();

builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<IAntiforgery>(new CookielessAntiforgery());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BoardService>();
builder.Services.AddSingleton<CanvasService>();
builder.Services.AddSingleton<ConnectionTracker>();
builder.Services.AddHealthChecks();

builder.Services.AddResponseCompression(compression =>
{
    compression.EnableForHttps = true;
    compression.Providers.Add<BrotliCompressionProvider>();
    compression.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(brotli => brotli.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(gzip => gzip.Level = CompressionLevel.Fastest);

builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<IOptions<ChaosOptions>>((limiter, chaos) => RequestLimits.Configure(limiter, chaos.Value));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(circuit =>
    {
        circuit.DisconnectedCircuitMaxRetained = 20;
        circuit.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
        circuit.PersistedCircuitInMemoryMaxRetained = 20;
        circuit.PersistedCircuitInMemoryRetentionPeriod = TimeSpan.FromMinutes(5);
        circuit.MaxBufferedUnacknowledgedRenderBatches = 2;
        circuit.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
    })
    .AddHubOptions(hub => hub.MaximumReceiveMessageSize = 32 * 1024);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseResponseCompression();
app.UseRateLimiter();
app.UseWebSockets();
app.UseHubGuard();
app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(server => server.DisableWebSocketCompression = true);

await app.RunAsync();

public partial class Program;
