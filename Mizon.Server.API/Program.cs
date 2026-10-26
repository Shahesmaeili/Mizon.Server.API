using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server;
using Mizon.Server.API;
using Mizon.Server.API.Endpoints;
using Mizon.Server.API.Hubs;
using Mizon.Server.API.JsonSerializerContexts;
using Mizon.Server.API.Middlewares;
using Mizon.Server.Logic;
using System.Diagnostics;
using System.Net;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
     options.SerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default);
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddMizonServer();
builder.Services.AddMizonLogic();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<RequestMetadata>();
builder.Services.AddDbContextPool<MizonDatabaseContext>((sp, options) =>
{
    options.UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True");
});

builder.Services
    .AddAuthentication("jwt")
    .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("jwt", _ => {  });

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
    options.EnableDetailedErrors = true;
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

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5128, listenOptions =>
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
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddOutputCache();


var app = builder.Build();

// استفاده از CORS قبل از Authentication
app.UseCors("SignalRCors");

// اضافه کردن مسیر Hub
app.MapHub<MizonHub>("/mizonHub");

app.UseRequestDecompression();
app.UseResponseCompression();
app.UseAuthentication();


app.UseAuthorization();
app.UseOutputCache();

app.Use(async (context, next) =>
{

    var sw = Stopwatch.StartNew();

    var protocol = context.Request.Protocol;
    Console.WriteLine($"Request protocol: {protocol} - {context.GetEndpoint()!.DisplayName}");

    //context.Request.EnableBuffering();

    //string requestBody = string.Empty;

    //using (var reader = new StreamReader(
    //    context.Request.Body,
    //    encoding: System.Text.Encoding.UTF8,
    //    detectEncodingFromByteOrderMarks: false,
    //    leaveOpen: true))
    //{
    //    requestBody = await reader.ReadToEndAsync();
    //    context.Request.Body.Position = 0;
    //}

    //Console.WriteLine($"Request body: {requestBody}");

    await next();
    sw.Stop();

    var cacheStatus = context.Response.Headers["x-output-cache"].ToString();

    var elapsedMs = sw.Elapsed.TotalMilliseconds;
    var statusCode = context.Response.StatusCode;

    Console.WriteLine($"Response status: {statusCode} | Duration: {elapsedMs:F2} ms | Cache: {cacheStatus}");

});






app.UseMiddleware<ClientContextMiddleware>();

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


