using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server;
using Mizon.Server.API;
using Mizon.Server.API.Caching;
using Mizon.Server.API.Endpoints;
using Mizon.Server.API.Hubs;
using Mizon.Server.API.JsonSerializerContexts;
using Mizon.Server.API.Middlewares;
using Mizon.Server.Logic;
using Mizon.Server.Logic.Operations.Medias;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default);
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();
builder.Services.AddMizonServer();
builder.Services.AddMizonLogic();
builder.Services.AddMizonResponseCaching(builder.Configuration);
var mediaRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var localMediaRoots = new[]
    {
        Path.Combine(mediaRoot, "MusicHomeArtwork"),
        Path.Combine(mediaRoot, "MoodEmojis"),
        Path.Combine(mediaRoot, "PersonProfileFixtures"),
    }
    .Concat(builder.Configuration.GetSection("Media:LocalContentRoots").GetChildren()
        .Select(section => section.Value).OfType<string>());
builder.Services.AddSingleton(new LocalMediaContentStore(localMediaRoots));
builder.Services.AddSingleton(new PublicPeerMediaManifestStore(builder.Configuration["PeerMedia:PublicManifestDirectory"]));

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<RequestMetadata>();
builder.Services.AddDbContextPool<MizonDatabaseContext>(options =>
{
    options.UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True");
});

builder.Services
    .AddAuthentication("jwt")
    .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("jwt", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
}); 

builder.WebHost.UseQuic();   // این خط مهم است

// تنظیمات SignalR با پشتیبانی از JWT
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 102400;
    options.StreamBufferCapacity = 10;

    // تنظیمات مخصوص HTTP/3
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
}).AddJsonProtocol(options =>
{
    // تنظیمات JSON برای SignalR
    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default);
});

if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("TaskbarAudience:UseDemoCounts"))
{
    builder.Services.AddSingleton<TaskbarAudienceDemo>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<TaskbarAudienceDemo>());
}

// اضافه کردن CORS برای SignalR (اگر نیاز باشد)
builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRCors", policy =>
    {
        policy.AllowAnyOrigin() // آدرس کلاینت شما
              .AllowAnyMethod()
              .AllowAnyHeader();
             
    });
});

// A development-only configuration can own the listeners, including an HTTPS
// listener shared by localhost and the LAN. Avoid binding that port twice.
var useConfiguredDevelopmentEndpoints = builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Server:UseConfiguredDevelopmentEndpoints") &&
    builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any();
using var developmentLocalhostCertificate = useConfiguredDevelopmentEndpoints
    ? ReadDevelopmentLocalhostCertificate(builder.Configuration["Server:DevelopmentLocalhostCertificateThumbprint"])
    : null;
if (useConfiguredDevelopmentEndpoints)
{
    builder.WebHost.UseKestrelHttpsConfiguration();
    if (developmentLocalhostCertificate is not null)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var endpoints = context.Configuration.GetSection("Kestrel:Endpoints");
            var loader = options.ConfigurationLoader!;
            foreach (var endpoint in endpoints.GetChildren())
            {
                loader.Endpoint(endpoint.Key, configuration =>
                {
                    if (!configuration.IsHttps) return;
                    var https = configuration.HttpsOptions;
                    var serverCertificate = https.ServerCertificate;
                    if (serverCertificate is null) return;

                    // Let Kestrel select the ALPN for TCP and QUIC independently.
                    https.ServerCertificateSelector = (_, hostName) =>
                        string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase)
                            ? developmentLocalhostCertificate
                            : serverCertificate;
                });
            }
        });
    }
}
else
{
    var port = builder.Configuration.GetValue("Server:Port", 5128);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(port, listenOptions =>
        {
            listenOptions.UseHttps();
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        });
    });
}

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true; // چون تو HTTPS داری
});
builder.Services.AddRequestDecompression();


var app = builder.Build();

// Assets are provisioned locally by a business operation, never by a public endpoint.
var moodCatalogPath = Path.Combine(app.Environment.ContentRootPath, "App_Data", "MoodEmojis", "catalog.json");
if (builder.Configuration.GetValue("AssetInitialization:Enabled", true) && File.Exists(moodCatalogPath))
{
    await using var scope = app.Services.CreateAsyncScope();
    var imported = await scope.ServiceProvider.GetRequiredService<ClientMoodInitializeOperation>()
        .ExecuteAsync(moodCatalogPath, app.Lifetime.ApplicationStopping);
    app.Logger.LogInformation("Mood catalog is ready; {Imported} new moods registered.", imported);
}

// استفاده از CORS قبل از Authentication
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseCors("SignalRCors");
app.UseMizonRequestLocalization();

// اضافه کردن مسیر Hub
app.MapHub<MizonHub>("/mizonHub");

app.UseRequestDecompression();
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ClientContextMiddleware>();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
ClientEndpoints.Map(app);
MusicEndpoints.Map(app);
PersonEndpoints.Map(app);
MediaEndpoints.Map(app);
StoryEndpoints.Map(app);
SearchEndpoints.Map(app);

app.Run();

static X509Certificate2? ReadDevelopmentLocalhostCertificate(string? thumbprint)
{
    if (string.IsNullOrWhiteSpace(thumbprint)) return null;

    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly);
    var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Trim(), validOnly: true);
    if (matches.Count == 1 && matches[0].HasPrivateKey) return matches[0];

    foreach (var certificate in matches) certificate.Dispose();
    throw new InvalidOperationException("The configured development localhost certificate was not found or has no private key.");
}
