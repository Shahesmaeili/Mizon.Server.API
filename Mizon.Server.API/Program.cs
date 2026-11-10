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

// Keep the existing development endpoint; allow isolated diagnostics/deployments.
var port = builder.Configuration.GetValue("Server:Port", 5128);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(port, listenOptions =>
    {
        listenOptions.UseHttps();
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
    });
});

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
MediaEndpoints.Map(app);
StoryEndpoints.Map(app);
SearchEndpoints.Map(app);

app.Run();
