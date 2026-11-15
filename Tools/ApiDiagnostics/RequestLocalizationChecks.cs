using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mizon.Server;
using Mizon.Server.API.Caching;
using Mizon.Server.API.JsonSerializerContexts;
using Mizon.Server.API.Middlewares;
using Mizon.Server.Logic.Operations.Clients;
using Mizon.Server.Logic.Requests.Clients;
using Mizon.Server.Logic.Responses.Clients;

internal static class RequestLocalizationChecks
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolver = MizonJsonContexts.Default);
        builder.Services.AddMizonServer();
        builder.Services.AddMizonResponseCaching(builder.Configuration);
        // Invalid registration input exits before storage. Login uses a synthetic
        // credentials error to exercise the HTTP response path without a real account.
        builder.Services.AddScoped(_ => new RegisterOperation(null!));
        builder.Services.AddScoped<LoginFixture>();

        await using var app = builder.Build();
        app.UseMizonRequestLocalization();
        app.MapGet("/login-fixture", (IApiOperationExecutor operations, CancellationToken token) =>
            operations.ExecuteAsync<LoginFixture, LoginRequest, LoginResponse>(new(), null, token));
        app.MapPost("/register-fixture",
            (BaseApiRequest<RegisterRequest> request, IApiOperationExecutor operations, CancellationToken token) =>
                operations.ExecuteAsync<RegisterOperation, RegisterRequest, RegisterResponse>(request, null, token));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
        var cases = new (string? Header, string Expected)[]
        {
            (null, "en-US"), ("en-US", "en-US"), ("fa-IR", "fa-IR"), ("FA", "fa-IR"),
            ("fr-FR", "en-US"), ("fr, fa;q=0.4, en;q=0.3", "fa-IR"), ("fa;q=0, en;q=0.1", "en-US"),
            ("fa;q=0.8, en;q=0.9", "en-US"), ("fa-AF", "fa-IR"), ("***;q=oops", "en-US")
        };
        await Task.WhenAll(cases.Select(async item =>
        {
            var error = await ReadErrorAsync("/login-fixture", item.Header, item.Expected);
            Require(error.GetProperty("code").GetInt32() == 1001, "login error keeps its code");
            Require(error.GetProperty("title").GetString() == (item.Expected == "fa-IR" ? "ورود ناموفق بود" : "Login Failed"),
                "concurrent requests return the appropriate language");
        }));

        const string registration = """{"content":{"firstName":"Jane","lastName":"Example","username":"valid.user","emailAddress":"jane@example.invalid","password":"short"}}""";
        var english = await ReadErrorAsync("/register-fixture", "en-US", "en-US", registration);
        var persian = await ReadErrorAsync("/register-fixture", "fa-IR", "fa-IR", registration);
        Require(english.GetProperty("code").GetInt32() == 21000 && persian.GetProperty("code").GetInt32() == 21000,
            "registration keeps its validation code");
        Require(english.GetProperty("details").GetString() == "Password must contain 8 to 128 characters."
            && persian.GetProperty("details").GetString() == "رمز عبور باید بین ۸ تا ۱۲۸ نویسه داشته باشد.",
            "registration preserves the specific validation reason in both languages");
        Console.WriteLine("PASS request localization: HTTP negotiation, fallback, concurrent isolation and localized authentication errors.");

        async Task<JsonElement> ReadErrorAsync(string path, string? header, string expected, string? body = null)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
            if (header is not null) request.Headers.TryAddWithoutValidation("Accept-Language", header);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Require(response.Content.Headers.ContentLanguage.Single() == expected, $"Content-Language for {header}");
            Require(response.Headers.Vary.Contains("Accept-Language"), "responses declare the language negotiation header");
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            return json.RootElement.GetProperty("error").Clone();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LoginFixture : IApiOperation<LoginRequest, LoginResponse>
    {
        public Task<BaseApiResponse<LoginResponse>> HandleAsync(BaseApiRequest<LoginRequest> request,
            RequestMetadata? requestMetadata, CancellationToken cancellationToken) =>
            Task.FromResult(new BaseApiResponse<LoginResponse>
            {
                Error = new() { Code = 1001, Title = "Login Failed", Details = "Email or password is incorrect." }
            });
    }
}
