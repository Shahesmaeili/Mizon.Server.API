using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mizon.Server;
using Mizon.Server.API;
using Mizon.Server.API.Middlewares;
using Mizon.Server.API.Services;
using Mizon.Server.Logic.Operations.Clients;
using Mizon.Server.Logic.Requests.Clients;
using Mizon.Server.Logic.Responses.Clients;

internal static class AuthChecks
{
    public static async Task RunAsync()
    {
        // A synthetic signing key; the real appsettings secret is never read.
        var key = SHA256.HashData("Mizon API authentication regression fixture"u8);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Convert.ToBase64String(key),
            ["Jwt:Issuer"] = "fixture-issuer",
            ["Jwt:Audience"] = "fixture-audience",
            ["Jwt:AccessTokenLifetime"] = "00:15:00"
        }).Build();
        var jwt = new JwtTokenService(configuration);
        var clientId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        JwtTokenService.JwtPayload Payload() => new()
        {
            sub = clientId.ToString(), sid = sessionId.ToString(),
            iss = "fixture-issuer", aud = "fixture-audience", iat = now, exp = now + 900
        };
        var validPayload = Payload();
        var payloadJson = JsonSerializer.Serialize(validPayload, AuthJsonContext.Default.JwtPayload);
        var known = Sign("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", payloadJson, key);
        var generated = jwt.GenerateAccessToken(clientId, sessionId);
        CheckValid("independently signed token", known);
        CheckValid("generated token", generated);
        var generatedParts = generated.Split('.');
        Require(generatedParts[0] == Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"), "issued header wire format");
        var generatedUnsigned = generatedParts[0] + "." + generatedParts[1];
        Require(generatedParts[2] == Signature(generatedUnsigned, key), "issued HMAC wire format");
        using (var generatedBody = JsonDocument.Parse(Decode(generatedParts[1])))
        {
            var payload = generatedBody.RootElement;
            Require(payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64() == 900,
                "configured token lifetime");
        }

        foreach (var malformed in new[] { "", " ", ".", "..", "a", "a.b", "a.b.c.d", "!.e30.x", "null.e30.x" })
            CheckError("malformed token", malformed, JwtTokenService.JwtValidationError.InvalidFormat);
        CheckError("wrong algorithm", Sign("{\"alg\":\"none\"}", payloadJson, key), JwtTokenService.JwtValidationError.InvalidFormat);
        CheckError("null header", Sign("null", payloadJson, key), JwtTokenService.JwtValidationError.InvalidFormat);
        var parts = known.Split('.');
        var corrupted = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        CheckError("tampered signature", parts[0] + "." + parts[1] + "." + corrupted, JwtTokenService.JwtValidationError.InvalidSignature);
        CheckError("padded signature remains invalid", known + "=", JwtTokenService.JwtValidationError.InvalidSignature);
        CheckError("Unicode signature", parts[0] + "." + parts[1] + "." + new string('é', 43), JwtTokenService.JwtValidationError.InvalidSignature);
        CheckError("bad payload JSON", Sign("{\"alg\":\"HS256\"}", "{", key), JwtTokenService.JwtValidationError.InvalidPayload);
        CheckError("null payload", Sign("{\"alg\":\"HS256\"}", "null", key), JwtTokenService.JwtValidationError.InvalidPayload);

        var expired = Payload(); expired.exp = now - 60;
        CheckPayload("expired", expired, JwtTokenService.JwtValidationError.Expired);
        var future = Payload(); future.iat = now + 300;
        CheckPayload("future issue time", future, JwtTokenService.JwtValidationError.InvalidPayload);
        var issuer = Payload(); issuer.iss = "other";
        CheckPayload("issuer", issuer, JwtTokenService.JwtValidationError.InvalidIssuer);
        var audience = Payload(); audience.aud = "other";
        CheckPayload("audience", audience, JwtTokenService.JwtValidationError.InvalidAudience);
        var invalidSub = Payload(); invalidSub.sub = "not-a-guid";
        CheckPayload("subject", invalidSub, JwtTokenService.JwtValidationError.InvalidPayload);
        var invalidSession = Payload(); invalidSession.sid = "not-a-guid";
        CheckPayload("session", invalidSession, JwtTokenService.JwtValidationError.InvalidPayload);

        var longPayload = payloadJson[..^1] + ",\"ignored\":\"" + new string('x', 4096) + "\"}";
        CheckValid("pooled large payload", Sign("{\"alg\":\"HS256\"}", longPayload, key));
        // The previous decoder accepted conventional padded Base64 header/payload.
        var paddedUnsigned = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}")) + "."
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        CheckValid("legacy padded Base64 segments", paddedUnsigned + "." + Signature(paddedUnsigned, key));

        // Warm up then measure only validation; never log a token/key.
        for (var i = 0; i < 1000; i++) jwt.ValidateToken(known);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < 10000; i++)
            if (!jwt.ValidateToken(known).IsValid) throw new InvalidOperationException("Validation benchmark failed.");
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"AUTH validation: {elapsed.TotalMicroseconds / 10000:F2} us/token; {allocated / 10000.0:F0} allocated bytes/token (warm synchronous loop).");

        await CheckHandlerAsync(jwt, known, clientId, sessionId);
        await CheckClientContextAsync(clientId, sessionId);
        Console.WriteLine("PASS auth: token contracts, malformed input, error categories, middleware cache and cancellation.");

        void CheckValid(string label, string token)
        {
            var result = jwt.ValidateToken(token);
            Require(result.IsValid && result.Error == JwtTokenService.JwtValidationError.None
                && result.ClientPublicId == clientId && result.SessionPublicId == sessionId, label);
        }
        void CheckError(string label, string token, JwtTokenService.JwtValidationError error)
        {
            var result = jwt.ValidateToken(token);
            Require(!result.IsValid && result.Error == error, label);
        }
        void CheckPayload(string label, JwtTokenService.JwtPayload payload, JwtTokenService.JwtValidationError error) =>
            CheckError(label, Sign("{\"alg\":\"HS256\"}",
                JsonSerializer.Serialize(payload, AuthJsonContext.Default.JwtPayload), key), error);
    }

    private static async Task CheckHandlerAsync(JwtTokenService jwt, string token, Guid clientId, Guid sessionId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(jwt);
        services.AddAuthentication("jwt").AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("jwt", _ => { });
        await using var provider = services.BuildServiceProvider();
        foreach (var source in new[] { "header", "access_token", "token" })
        {
            await using var scope = provider.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            if (source == "header") context.Request.Headers.Authorization = "Bearer   " + token + "  ";
            else context.Request.QueryString = new QueryString("?" + source + "=" + token);
            var result = await context.AuthenticateAsync("jwt");
            Require(result.Succeeded
                && result.Principal!.FindFirstValue("sub") == clientId.ToString()
                && result.Principal.FindFirstValue("sid") == sessionId.ToString()
                && result.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == clientId.ToString()
                && result.Principal.FindFirstValue(ClaimTypes.UserData) == sessionId.ToString(), "handler " + source);
        }
        await using var emptyScope = provider.CreateAsyncScope();
        var empty = new DefaultHttpContext { RequestServices = emptyScope.ServiceProvider };
        Require((await empty.AuthenticateAsync("jwt")).None, "missing-token handler result");
    }

    private static async Task CheckClientContextAsync(Guid clientId, Guid sessionId)
    {
        var nextCalls = 0;
        var middleware = new ClientContextMiddleware(_ => { nextCalls++; return Task.CompletedTask; });
        var executor = new FindClientExecutor();
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", clientId.ToString()), new Claim("sid", sessionId.ToString())], "jwt")),
            RequestAborted = cancellation.Token
        };
        context.Request.Headers["Mizon-Device-Name"] = "fixture";
        context.Request.Headers["Mizon-OS-Version"] = "10.0.26100.1";
        var platform = Guid.NewGuid();
        context.Request.Headers["Mizon-Platform-Version"] = platform.ToString();
        try
        {
            var metadata = new RequestMetadata();
            await middleware.InvokeAsync(context, executor, metadata);
            Require(executor.Calls == 1 && executor.LastToken == cancellation.Token && metadata.ClientId == 123
                && metadata.ClientPublicId == clientId && metadata.SessionPublicId == sessionId
                && metadata.DeviceName == "fixture" && metadata.PlatformVersion == platform
                && metadata.OsVersion == new Version(10, 0, 26100, 1), "cache miss and metadata");
            await middleware.InvokeAsync(context, executor, new RequestMetadata());
            Require(executor.Calls == 1 && nextCalls == 2, "cache hit avoids executor");
            ClientContextMiddleware.Clients.TryRemove(clientId, out _);
            cancellation.Cancel();
            var cancelledMetadata = new RequestMetadata();
            try
            {
                await middleware.InvokeAsync(context, executor, cancelledMetadata);
                throw new InvalidOperationException("Middleware ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
                Require(executor.LastToken == cancellation.Token && nextCalls == 2
                    && cancelledMetadata.ClientId is null && !ClientContextMiddleware.Clients.ContainsKey(clientId),
                    "cancelled miss does not cache or invoke next");
            }
            var anonymous = new DefaultHttpContext();
            var before = executor.Calls;
            await middleware.InvokeAsync(anonymous, executor, new RequestMetadata());
            Require(executor.Calls == before && nextCalls == 3, "anonymous metadata does not query");
        }
        finally
        {
            ClientContextMiddleware.Clients.TryRemove(clientId, out _);
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    private static byte[] Decode(string value)
    {
        var normalized = value.Replace("-", "+").Replace("_", "/");
        return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
    }
    private static string Signature(string unsigned, byte[] key) => Convert.ToBase64String(
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(unsigned))).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    private static string Sign(string header, string payload, byte[] key)
    {
        var unsigned = Encode(header) + "." + Encode(payload);
        return unsigned + "." + Signature(unsigned, key);
    }
    private static void Require(bool valid, string label)
    {
        if (!valid) throw new InvalidOperationException("Authentication regression: " + label);
    }

    private sealed class FindClientExecutor : IApiOperationExecutor
    {
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<BaseApiResponse<TResponse>> ExecuteAsync<TOperation, TRequest, TResponse>(
            BaseApiRequest<TRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken)
            where TOperation : class, IApiOperation<TRequest, TResponse>
            where TRequest : IApiRequest
            where TResponse : IApiResponse
        {
            Calls++;
            LastToken = cancellationToken;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<BaseApiResponse<TResponse>>(cancellationToken);
            if (typeof(TOperation) != typeof(FindClientIdOperation))
                throw new InvalidOperationException("Unexpected test operation.");
            return Task.FromResult((BaseApiResponse<TResponse>)(object)new BaseApiResponse<FindClientIdResponse>
            {
                Content = new() { ClientId = 123 }
            });
        }
    }
}

[JsonSerializable(typeof(JwtTokenService.JwtPayload))]
internal partial class AuthJsonContext : JsonSerializerContext;

