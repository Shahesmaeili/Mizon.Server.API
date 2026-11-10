using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mizon.Server;
using Mizon.Server.API.Caching;

internal static class ResponseCacheChecks
{
    public static async Task RunAsync()
    {
        // Real Kestrel and real OutputCache middleware, but synthetic identities
        // and handlers: no database, credentials or persistent rows are touched.
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMizonServer();
        builder.Services.AddScoped<RequestMetadata>();
        builder.Services.AddMizonResponseCaching(builder.Configuration);
        var fixture = new Fixture();
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (!Guid.TryParse(context.Request.Headers["X-Fixture-Client"], out var client)
                || !Guid.TryParse(context.Request.Headers["X-Fixture-Session"], out var session))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", client.ToString()), new Claim("sid", session.ToString())
            ], "fixture"));
            var metadata = context.RequestServices.GetRequiredService<RequestMetadata>();
            metadata.ClientPublicId = client;
            metadata.SessionPublicId = session;
            metadata.ClientId = client == Fixture.ClientOne ? 1 : 2;
            await next(context);
        });
        app.UseOutputCache();
        foreach (var path in new[] { "/Musics/Music", "/Clients/Me", "/Clients/ClientMoodTypes", "/Search/Global", "/fixture/unlisted" })
            app.MapGet(path, (HttpContext context) => fixture.HandleAsync(context));
        foreach (var path in new[] { "/Musics/MusicLibrary", "/Musics/MusicView", "/Musics/MusicPlaylistMembership" })
            app.MapPost(path, (HttpContext context) => fixture.HandleAsync(context));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15)
        };
        var versions = app.Services.GetRequiredService<ResponseCacheVersions>();
        try
        {
            var first = await SendAsync("/Musics/Music?case=get&MusicPublicId=one");
            var cached = await SendAsync("/Musics/Music?case=get&MusicPublicId=one");
            Require(first.Body == cached.Body && fixture.Count("get") == 1, "GET response bytes are cached");
            Require(cached.Age.HasValue, "cache hit is served by the actual output middleware");
            Require(cached.CacheControl?.Contains("no-store", StringComparison.OrdinalIgnoreCase) == true,
                "personal responses are not advertised for browser/proxy caching");

            var otherQuery = await SendAsync("/Musics/Music?case=get&MusicPublicId=two");
            var otherClient = await SendAsync("/Musics/Music?case=get&MusicPublicId=one", client: Fixture.ClientTwo);
            var otherSession = await SendAsync("/Musics/Music?case=get&MusicPublicId=one", session: Fixture.SessionTwo);
            Require(new[] { first.Body, otherQuery.Body, otherClient.Body, otherSession.Body }.Distinct().Count() == 4,
                "query, client and session each isolate personalized results");
            Require((await SendAsync("/Musics/Music?case=get&MusicPublicId=one")).Body == first.Body,
                "another identity cannot replace the original client's cache");
            var deviceOne = await SendAsync("/Clients/Me?case=device", device: "device-one");
            var deviceTwo = await SendAsync("/Clients/Me?case=device", device: "device-two");
            Require(deviceOne.Body != deviceTwo.Body && fixture.Count("device") == 2,
                "device headers do not cross-contaminate cached responses");

            var shared = await SendAsync("/Clients/ClientMoodTypes?case=catalog");
            var sharedAgain = await SendAsync("/Clients/ClientMoodTypes?case=catalog", client: Fixture.ClientTwo, session: Fixture.SessionTwo);
            Require(shared.Body == sharedAgain.Body && fixture.Count("catalog") == 1,
                "explicitly shared catalogs can reuse bytes across authenticated users");
            var anonymous = await SendAsync("/Musics/Music?case=get&MusicPublicId=one", authenticated: false);
            Require(anonymous.Status == HttpStatusCode.Unauthorized, "warm cache never bypasses authentication");
            Console.WriteLine("PASS response cache: actual GET hits, query/client/session isolation, shared catalog and authorization ordering.");

            const string bodyOne = "{\"Content\":{\"PageSize\":1}}";
            const string bodyTwo = "{\"Content\":{\"PageSize\":2}}";
            var post = await SendAsync("/Musics/MusicLibrary?case=post", body: bodyOne);
            var postHit = await SendAsync("/Musics/MusicLibrary?case=post", body: bodyOne);
            var postOther = await SendAsync("/Musics/MusicLibrary?case=post", body: bodyTwo);
            Require(post.Body == postHit.Body && postOther.Body != post.Body && fixture.Count("post") == 2,
                "read-only POST caches by body without colliding different requests");
            Require(Parse(post.Body).BodyHash == Hash(bodyOne) && Parse(postOther.Body).BodyHash == Hash(bodyTwo),
                "body hashing rewinds the stream for the handler");
            var largeBody = new string('x', 70 * 1024);
            var large = await SendAsync("/Musics/MusicLibrary?case=large", body: largeBody);
            var largeAgain = await SendAsync("/Musics/MusicLibrary?case=large", body: largeBody);
            Require(large.Body != largeAgain.Body && fixture.Count("large") == 2 && Parse(large.Body).BodyHash == Hash(largeBody),
                "oversized bodies bypass caching without losing request bytes");
            var chunked = await SendAsync("/Musics/MusicLibrary?case=chunked", body: bodyOne, chunked: true);
            var chunkedAgain = await SendAsync("/Musics/MusicLibrary?case=chunked", body: bodyOne, chunked: true);
            Require(chunked.Body != chunkedAgain.Body && fixture.Count("chunked") == 2 && Parse(chunked.Body).BodyHash == Hash(bodyOne),
                "unknown-length bodies bypass caching without consuming request data");
            Console.WriteLine("PASS response cache: read-only POST keys, preserved request body and large-body bypass.");

            var original = await SendAsync("/Musics/Music?case=revalidate");
            var refreshed = await SendAsync("/Musics/Music?case=revalidate", cacheControl: "no-cache");
            Require(original.Body != refreshed.Body && (await SendAsync("/Musics/Music?case=revalidate")).Body == refreshed.Body,
                "no-cache refreshes the stored response");
            var notStored = await SendAsync("/Musics/Music?case=revalidate", cacheControl: "no-store");
            Require(notStored.Body != refreshed.Body && (await SendAsync("/Musics/Music?case=revalidate")).Body == refreshed.Body,
                "no-store bypasses lookup and does not replace stored bytes");

            foreach (var (path, label, body) in new[]
            {
                ("/Musics/MusicView", "write", "{}"),
                ("/Musics/MusicPlaylistMembership", "membership", "{}"),
                ("/fixture/unlisted", "unknown", (string?)null),
                ("/Search/Global", "stream", (string?)null),
                ("/Musics/Music", "error", (string?)null),
                ("/Musics/Music", "status", (string?)null),
                ("/Musics/Music", "cookie", (string?)null),
                ("/Musics/Music", "no-outcome", (string?)null),
                ("/Musics/Music", "expired", (string?)null),
                ("/Musics/Music", "large-response", (string?)null)
            })
            {
                var url = path + "?case=" + label;
                var a = await SendAsync(url, body: body);
                var b = await SendAsync(url, body: body);
                Require(a.Body != b.Body && fixture.Count(label) == 2, label + " response is never cached");
            }
            Console.WriteLine("PASS response cache: writes, unknown routes, search stream, HTTP200 business errors, failures, cookies and expired data bypass storage.");

            var expiring = await SendAsync("/Musics/Music?case=expiry");
            Require((await SendAsync("/Musics/Music?case=expiry")).Body == expiring.Body,
                "short-lived successful response initially hits cache");
            await Task.Delay(450);
            Require((await SendAsync("/Musics/Music?case=expiry")).Body != expiring.Body && fixture.Count("expiry") == 2,
                "content expiry clamps middleware TTL before first response byte");

            var slowResponse = await SendAsync("/Musics/Music?case=slow-expiry");
            var originalDeadline = Parse(slowResponse.Body).ExpiresAt
                ?? throw new InvalidOperationException("Slow-response fixture must expose its original expiry.");
            Require(DateTimeOffset.UtcNow < originalDeadline, "slow response completes before its original expiry");
            var untilOriginalExpiry = originalDeadline - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(75);
            if (untilOriginalExpiry > TimeSpan.Zero) await Task.Delay(untilOriginalExpiry);
            var afterSlowExpiry = await SendAsync("/Musics/Music?case=slow-expiry");
            Require(slowResponse.Body != afterSlowExpiry.Body && fixture.Count("slow-expiry") == 2,
                "slow serialization cannot extend expiry by the time between first flush and cache storage");

            var concurrent = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => SendAsync("/Musics/Music?case=stampede")));
            Require(fixture.Count("stampede") == 1 && concurrent.Select(x => x.Body).Distinct().Count() == 1,
                "16 simultaneous misses execute one producer");
            Console.WriteLine("PASS response cache: real expiry and 16-request stampede protection.");

            var toInvalidate = await SendAsync("/Musics/Music?case=invalidate");
            await versions.InvalidateAsync(ResponseCacheDomain.MusicCounters);
            var invalidated = await SendAsync("/Musics/Music?case=invalidate");
            Require(toInvalidate.Body != invalidated.Body && fixture.Count("invalidate") == 2,
                "a dependent mutation invalidates a completed read");
            Require((await SendAsync("/Clients/ClientMoodTypes?case=catalog")).Body == shared.Body && fixture.Count("catalog") == 1,
                "invalidating music counters keeps unrelated catalog entries");

            var racingRead = SendAsync("/Musics/Music?case=race");
            await fixture.RaceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await versions.InvalidateAsync(ResponseCacheDomain.MusicCounters);
            fixture.ReleaseRace.TrySetResult();
            var oldInFlight = await racingRead;
            var afterWrite = await SendAsync("/Musics/Music?case=race");
            Require(oldInFlight.Body != afterWrite.Body && fixture.Count("race") == 2,
                "a read finishing after invalidation cannot repopulate the current generation");
            Require((await SendAsync("/Musics/Music?case=race")).Body == afterWrite.Body,
                "current-generation results still cache normally");
            Console.WriteLine("PASS response cache: mutation invalidation and in-flight stale-population race.");

            using var cancellation = new CancellationTokenSource();
            var cancelledRead = SendAsync("/Musics/Music?case=cancel", token: cancellation.Token);
            await fixture.CancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await cancelledRead; throw new InvalidOperationException("Cancelled cache producer completed successfully."); }
            catch (OperationCanceledException) { }
            await fixture.CancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var recovered = await SendAsync("/Musics/Music?case=cancel");
            Require((await SendAsync("/Musics/Music?case=cancel")).Body == recovered.Body && fixture.Count("cancel") == 2,
                "cancelled producer leaves neither a cached response nor a stuck key lock");
            Console.WriteLine("PASS response cache: request cancellation releases producer and key for subsequent requests.");
        }
        finally
        {
            fixture.ReleaseRace.TrySetResult();
            await app.StopAsync();
        }

        async Task<Reply> SendAsync(string path, string? body = null, Guid? client = null, Guid? session = null,
            bool authenticated = true, CancellationToken token = default, string? device = null,
            string? cacheControl = null, bool chunked = false)
        {
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
            if (authenticated)
            {
                request.Headers.Add("X-Fixture-Client", (client ?? Fixture.ClientOne).ToString());
                request.Headers.Add("X-Fixture-Session", (session ?? Fixture.SessionOne).ToString());
            }
            if (device is not null) request.Headers.Add("Mizon-Device-Identifier", device);
            if (cacheControl is not null) request.Headers.CacheControl = CacheControlHeaderValue.Parse(cacheControl);
            if (body is not null) request.Content = chunked
                ? new UnknownLengthContent(body)
                : new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, token);
            return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync(token),
                response.Headers.Age, response.Headers.CacheControl?.ToString());
        }
    }

    private static CacheFixtureReply Parse(string body) =>
        JsonSerializer.Deserialize(body, CacheFixtureJsonContext.Default.CacheFixtureReply)!;
    private static string Hash(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("Response cache: " + check);
    }

    private sealed record Reply(HttpStatusCode Status, string Body, TimeSpan? Age, string? CacheControl);

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        public UnknownLengthContent(string body)
        {
            _bytes = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();
    }

    private sealed class Fixture
    {
        public static readonly Guid ClientOne = Guid.Parse("c3fe1327-d355-4ba6-9be6-9625217a0001");
        public static readonly Guid ClientTwo = Guid.Parse("c3fe1327-d355-4ba6-9be6-9625217a0002");
        public static readonly Guid SessionOne = Guid.Parse("f265bb97-5965-4396-8052-9ec07ec50001");
        public static readonly Guid SessionTwo = Guid.Parse("f265bb97-5965-4396-8052-9ec07ec50002");
        private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);
        public TaskCompletionSource RaceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRace { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count(string label) => _calls.GetValueOrDefault(label);

        public async Task HandleAsync(HttpContext context)
        {
            var label = context.Request.Query["case"].ToString();
            var call = _calls.AddOrUpdate(label, 1, static (_, count) => count + 1);
            if (label == "race" && call == 1)
            {
                RaceStarted.TrySetResult();
                await ReleaseRace.Task.WaitAsync(context.RequestAborted);
            }
            if (label == "cancel" && call == 1)
            {
                CancelStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                finally { CancelObserved.TrySetResult(); }
            }
            if (label == "stampede") await Task.Delay(150, context.RequestAborted);
            if (label == "status") context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            if (label == "cookie") context.Response.Headers.SetCookie = "fixture=value; HttpOnly";
            if (label == "stream") context.Response.Headers.CacheControl = "no-store";
            DateTimeOffset? expiresAt = label switch
            {
                "expiry" => DateTimeOffset.UtcNow.AddMilliseconds(300),
                "slow-expiry" => DateTimeOffset.UtcNow.AddMilliseconds(700),
                "expired" => DateTimeOffset.UtcNow.AddSeconds(-1),
                _ => null
            };
            if (label != "no-outcome") ResponseCacheOutcome.Set(context, label != "error", expiresAt);
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            var reply = new CacheFixtureReply(call, Hash(body), label == "large-response" ? new string('x', 3 * 1024 * 1024) : null, expiresAt);
            context.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(reply, CacheFixtureJsonContext.Default.CacheFixtureReply);
            if (label == "slow-expiry")
            {
                var split = json.Length / 2;
                await context.Response.WriteAsync(json[..split], context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(350, context.RequestAborted);
                await context.Response.WriteAsync(json[split..], context.RequestAborted);
            }
            else await context.Response.WriteAsync(json, context.RequestAborted);
        }
    }
}

internal sealed record CacheFixtureReply(int Call, string BodyHash, string? Padding = null, DateTimeOffset? ExpiresAt = null);
[JsonSerializable(typeof(CacheFixtureReply))]
internal partial class CacheFixtureJsonContext : JsonSerializerContext;
