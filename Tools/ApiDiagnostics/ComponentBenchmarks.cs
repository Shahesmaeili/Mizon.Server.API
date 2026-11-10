using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mizon.Server;
using Mizon.Server.API.Caching;
using Mizon.Server.API.Endpoints.Search;
using Mizon.Server.Logic.Responses.Search;

// Uses the real NDJSON writer, generated response metadata and Mizon's actual
// output-cache pipeline. All identities/payloads are synthetic; no DB is opened.
internal static class ComponentBenchmarks
{
    public static async Task RunAsync(string outputPath)
    {
        const int warmup = 500;
        const int batches = 11;
        const int iterations = 1000;
        var rows = Enumerable.Range(0, 20).Select(i => new GlobalSearchItemResponse
        {
            ItemType = i % 2 == 0 ? "music" : "client",
            PublicId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Title = "Mizon benchmark", PersianTitle = "آهنگ آزمایشی", Username = "fixture"
        }).ToArray();
        var results = new List<ComponentMeasurement>();
        using var output = new MemoryStream(16 * 1024);
        async ValueTask WriteRows()
        {
            output.SetLength(0);
            var writer = PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true));
            try
            {
                await NdjsonResponseWriter.WriteAsync(Rows(rows), writer,
                    ComponentBenchmarkJson.Default.GlobalSearchItemResponse, CancellationToken.None);
            }
            finally { await writer.CompleteAsync(); }
        }
        await WriteRows();
        var expectedLength = output.Length;
        if (expectedLength == 0) throw new InvalidOperationException("Empty NDJSON benchmark payload.");
        await Measure("Api.Ndjson20Rows", WriteRows);
        if (output.Length != expectedLength) throw new InvalidOperationException("NDJSON length changed.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMizonServer();
        builder.Services.AddScoped<RequestMetadata>();
        builder.Services.AddMizonResponseCaching(builder.Configuration);
        await using var app = builder.Build();
        var clientId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var sessionId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var identity = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", clientId.ToString()), new Claim("sid", sessionId.ToString())], "fixture"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(rows[0], ComponentBenchmarkJson.Default.GlobalSearchItemResponse);
        var calls = 0;
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = identity;
            var metadata = context.RequestServices.GetRequiredService<RequestMetadata>();
            metadata.ClientPublicId = clientId;
            metadata.SessionPublicId = sessionId;
            metadata.ClientId = 1;
            await next(context);
        });
        app.UseOutputCache();
        app.MapGet("/Musics/Music", async (HttpContext context) =>
        {
            Interlocked.Increment(ref calls);
            ResponseCacheOutcome.Set(context, true);
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
        });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false })
            { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
            async ValueTask Send(bool bypass)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/Musics/Music?MusicPublicId=fixture");
                if (bypass) request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (!bytes.AsSpan().SequenceEqual(payload)) throw new InvalidOperationException("HTTP payload changed.");
            }
            await Send(false);
            var beforeHits = calls;
            await Measure("Api.OutputCacheHitHttp", () => Send(false));
            if (calls != beforeHits) throw new InvalidOperationException("Cached requests unexpectedly reached the handler.");
            var beforeBypass = calls;
            await Measure("Api.OutputCacheBypassHttp", () => Send(true));
            if (calls - beforeBypass != warmup + batches * iterations)
                throw new InvalidOperationException("Bypassed requests unexpectedly used cache.");
        }
        finally { await app.StopAsync(); }
        var report = new ComponentBenchmarkReport(RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), warmup, batches, iterations, results);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, ComponentBenchmarkJson.Default.ComponentBenchmarkReport));

        async Task Measure(string name, Func<ValueTask> action)
        {
            for (var i = 0; i < warmup; i++) await action();
            var samples = new List<ComponentSample>(batches);
            for (var batch = 0; batch < batches; batch++)
            {
                var allocated = GC.GetTotalAllocatedBytes(true);
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) await action();
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000 / iterations;
                var allocation = (GC.GetTotalAllocatedBytes(true) - allocated) / (double)iterations;
                samples.Add(new(elapsed, allocation));
            }
            results.Add(new(name, samples));
            Console.WriteLine($"{name}: median {samples.Select(s => s.MicrosecondsPerOperation).Order().ElementAt(batches / 2):F3} us/op");
        }
    }

    private static async IAsyncEnumerable<GlobalSearchItemResponse> Rows(GlobalSearchItemResponse[] rows,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        await Task.CompletedTask;
        foreach (var row in rows) { token.ThrowIfCancellationRequested(); yield return row; }
    }
}

internal sealed record ComponentSample(double MicrosecondsPerOperation, double AllocatedBytesPerOperation);
internal sealed record ComponentMeasurement(string Name, List<ComponentSample> Samples);
internal sealed record ComponentBenchmarkReport(string Runtime, string Architecture, int Warmup, int Batches,
    int IterationsPerBatch, List<ComponentMeasurement> Results);
[JsonSerializable(typeof(ComponentBenchmarkReport))]
[JsonSerializable(typeof(GlobalSearchItemResponse))]
internal partial class ComponentBenchmarkJson : JsonSerializerContext;
