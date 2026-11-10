using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Mizon.Database;
using Mizon.Server.API.Services;

internal static class ResponseCacheBenchmarks
{
    private const int Rounds = 3;
    private const int MeasuredRequests = 50;

    public static async Task RunAsync(string baseUrl, string apiProjectDirectory, string outputDirectory,
        int serverProcessId, string? serverLogPath = null)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        if (!baseUri.IsLoopback || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Cache benchmarks require a local HTTPS API.");
        if (serverProcessId == Environment.ProcessId)
            throw new InvalidOperationException("The measured API must run in a separate process from this benchmark client.");
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        using var server = Process.GetProcessById(serverProcessId);
        var processStartedAt = server.StartTime.ToUniversalTime();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetFullPath(apiProjectDirectory))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables().Build();
        var dbOptions = new DbContextOptionsBuilder<MizonDatabaseContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("MIZON_BENCHMARK_CONNECTION")
                ?? "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True")
            .AddInterceptors(new SelectOnlyProbe()).Options;
        await using var fixtures = new MizonDatabaseContext(dbOptions);
        var client = await fixtures.ClientUsernames.AsNoTracking()
            .Where(x => x.Username == "alish").Where(x => x.IsActive)
            .Select(x => new { x.ClientId, x.Client!.PublicId }).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Existing alish fixture is required; this tool never seeds accounts.");
        var session = await fixtures.ClientSessions.AsNoTracking()
            .Where(x => x.ClientId == client.ClientId).Where(x => x.LogoutDateTime == null)
            .OrderByDescending(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("An existing active session is required; this tool never logs in or writes sessions.");
        var music = await fixtures.Musics.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("An existing active music fixture is required.");
        var file = await fixtures.MusicFiles.AsNoTracking().Where(x => x.Music!.PublicId == music)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            // The URI is restricted to loopback HTTPS before using this callback.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtTokenService(configuration).GenerateAccessToken(client.PublicId, session));
        var cases = new List<BenchmarkCase>
        {
            new("Me", "Clients/Me"),
            new("Music", $"Musics/Music?MusicPublicId={music:D}"),
            new("Library", "Musics/MusicLibrary", "{\"Content\":{\"PageSize\":5}}"u8.ToArray()),
            new("Features", "Musics/MusicFeatures", "{\"Content\":{\"Limit\":12}}"u8.ToArray()),
            new("Following", "Clients/FollowingActivities", "{\"Content\":{\"Scope\":\"music\",\"PageSize\":4}}"u8.ToArray()),
            new("Stories", "Stories/Stories")
        };
        if (file is Guid fileId) cases.Add(new("Waveform", $"Musics/MusicFileWaveform?MusicFilePublicId={fileId:D}"));
        else Console.WriteLine("SKIP cache benchmark Waveform: existing music has no file.");

        var report = new CacheBenchmarkReport
        {
            StartedUtc = DateTimeOffset.UtcNow,
            ServerProcessId = serverProcessId,
            ServerProcessStartedUtc = processStartedAt,
            Rounds = Rounds,
            RequestsPerBatch = MeasuredRequests,
            Notes = "Same live API and data: Cache-Control:no-store bypass versus normal OutputCache. "
                + "Latency includes HTTPS/HTTP2 transport and full response body, excludes client JSON/hash validation. "
                + "CPU is the API process only, not SQL Server or the benchmark client. "
                + "Working set/private bytes are process snapshots, not retained-cache size or allocations; JIT, GC and unrelated background work can affect them. "
                + "Cache capacity is an upper bound, not reserved RAM. Do not edit data or use this API process concurrently while comparing hashes. "
                + "Optional SQL counts count EF Executed DbCommand lines in a dedicated API log; they include any concurrent requests and are unavailable if logging is disabled."
        };
        var runId = Guid.NewGuid().ToString("N");
        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            // Warm TLS, the EF model, query plans and operation JIT equally before
            // measuring either mode. These requests are explicitly not cached.
            var warmPath = WithProbe(item.Path, runId + "-warm-" + item.Name);
            for (var warmup = 0; warmup < 10; warmup++)
            {
                var warm = await SendAsync(item, warmPath, bypass: true);
                await VerifyAsync(item, warm);
            }
        }

        for (var round = 1; round <= Rounds; round++)
        {
            foreach (var item in cases)
            {
                var path = WithProbe(item.Path, $"{runId}-{round}-{item.Name}");
                var modeOrder = round % 2 == 1 ? new[] { true, false } : new[] { false, true };
                foreach (var bypass in modeOrder)
                {
                    CacheBenchmarkMiss? firstMiss = null;
                    if (!bypass)
                    {
                        var miss = await SendAsync(item, path, bypass: false);
                        await VerifyAsync(item, miss);
                        if (miss.CacheHit) throw new InvalidOperationException("Unique benchmark key unexpectedly hit a previous cache entry.");
                        firstMiss = new(miss.ElapsedMilliseconds, miss.Body.Length, miss.ResponseHash);
                    }
                    // Identical warmup count per mode, outside all batch counters.
                    for (var warmup = 0; warmup < 3; warmup++)
                        await VerifyAsync(item, await SendAsync(item, path, bypass));
                    if (serverLogPath is not null) await Task.Delay(150);
                    var sqlBefore = CountSqlCommands(serverLogPath);
                    var before = Snapshot(server, processStartedAt);
                    var batchStarted = Stopwatch.GetTimestamp();
                    var samples = new List<CacheBenchmarkSample>(MeasuredRequests);
                    for (var index = 0; index < MeasuredRequests; index++)
                    {
                        var response = await SendAsync(item, path, bypass);
                        await VerifyAsync(item, response);
                        if (bypass && response.CacheHit)
                            throw new InvalidOperationException("No-store benchmark unexpectedly received a cached response.");
                        samples.Add(new(response.ElapsedMilliseconds, response.CacheHit, response.Body.Length, response.ResponseHash));
                    }
                    var wallMilliseconds = Stopwatch.GetElapsedTime(batchStarted).TotalMilliseconds;
                    var after = Snapshot(server, processStartedAt);
                    if (serverLogPath is not null) await Task.Delay(150);
                    var sqlAfter = CountSqlCommands(serverLogPath);
                    var ordered = samples.Select(x => x.LatencyMilliseconds).Order().ToArray();
                    var batch = new CacheBenchmarkBatch
                    {
                        Case = item.Name, Round = round, Mode = bypass ? "Bypass" : "Cached",
                        FirstMiss = firstMiss, Samples = samples,
                        MeanMilliseconds = ordered.Average(), MedianMilliseconds = Percentile(ordered, .5),
                        P95Milliseconds = Percentile(ordered, .95), BatchWallMilliseconds = wallMilliseconds,
                        CacheHits = samples.Count(x => x.CacheHit),
                        CpuMilliseconds = after.CpuMilliseconds - before.CpuMilliseconds,
                        Before = before, After = after,
                        SqlCommands = sqlBefore is long startCount && sqlAfter is long endCount ? endCount - startCount : null
                    };
                    report.Batches.Add(batch);
                    await SaveAsync();
                    Console.WriteLine(FormattableString.Invariant(
                        $"CACHE BENCH {item.Name} round={round} {batch.Mode}: mean={batch.MeanMilliseconds:F3}ms median={batch.MedianMilliseconds:F3}ms p95={batch.P95Milliseconds:F3}ms; hits={batch.CacheHits}/{MeasuredRequests}; API CPU={batch.CpuMilliseconds:F2}ms; SQL={batch.SqlCommands?.ToString() ?? "unmeasured"}"));
                }
            }
        }
        report.CompletedUtc = DateTimeOffset.UtcNow;
        await SaveAsync();
        Console.WriteLine($"PASS cache benchmark: {report.Batches.Sum(x => x.Samples.Count)} measured HTTP reads, identical response hashes; no database writes.");

        async Task<MeasuredResponse> SendAsync(BenchmarkCase item, string path, bool bypass)
        {
            using var request = new HttpRequestMessage(item.Body is null ? HttpMethod.Get : HttpMethod.Post, path)
            {
                Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            if (bypass) request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            if (item.Body is not null)
            {
                request.Content = new ByteArrayContent(item.Body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            var started = Stopwatch.GetTimestamp();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (response.StatusCode != HttpStatusCode.OK || response.Version != HttpVersion.Version20)
                throw new InvalidOperationException($"{item.Name}: expected HTTP/2 200, received {response.Version}/{(int)response.StatusCode}.");
            return new(bytes, elapsed, response.Headers.Age.HasValue, Convert.ToHexString(SHA256.HashData(bytes)));
        }

        async Task VerifyAsync(BenchmarkCase item, MeasuredResponse response)
        {
            using var document = JsonDocument.Parse(response.Body);
            var envelope = document.RootElement;
            if (!Property(envelope, "IsSuccess").GetBoolean() || Property(envelope, "Content").ValueKind == JsonValueKind.Null)
                throw new InvalidOperationException($"{item.Name} returned an error or null content; failures are not valid timing samples.");
            if (envelope.EnumerateObject().Any(x => x.Name.Equals("CacheExpiresAt", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Internal cache expiry metadata leaked into the API JSON.");
            if (expectedHashes.TryGetValue(item.Name, out var hash))
            {
                if (!StringComparer.Ordinal.Equals(hash, response.ResponseHash))
                    throw new InvalidOperationException($"{item.Name} response changed during the benchmark. Check concurrent data changes/expiry before interpreting the comparison.");
            }
            else
            {
                expectedHashes.Add(item.Name, response.ResponseHash);
                // Contains real account/content data. Keep this local evidence out
                // of git; credentials and bearer tokens are never persisted.
                await File.WriteAllBytesAsync(Path.Combine(outputDirectory, item.Name + ".response.json"), response.Body);
            }
        }

        Task SaveAsync() => File.WriteAllTextAsync(Path.Combine(outputDirectory, "cache-benchmark.json"),
            JsonSerializer.Serialize(report, CacheBenchmarkJsonContext.Default.CacheBenchmarkReport));
    }

    private static string WithProbe(string path, string probe) => path + (path.Contains('?') ? "&" : "?") + "cacheBenchmark=" + probe;
    private static double Percentile(double[] sorted, double value) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * value) - 1, 0, sorted.Length - 1)];
    private static JsonElement Property(JsonElement value, string name)
    {
        foreach (var property in value.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        throw new InvalidOperationException("Expected API envelope field was absent: " + name);
    }

    private static CacheProcessSnapshot Snapshot(Process process, DateTime expectedStart)
    {
        process.Refresh();
        if (process.HasExited || process.StartTime.ToUniversalTime() != expectedStart)
            throw new InvalidOperationException("The measured server process exited or changed during the test.");
        return new(DateTimeOffset.UtcNow, process.TotalProcessorTime.TotalMilliseconds, process.WorkingSet64, process.PrivateMemorySize64);
    }

    private static long? CountSqlCommands(string? path)
    {
        if (path is null) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        long count = 0;
        while (reader.ReadLine() is { } line)
            if (line.Contains("Executed DbCommand (", StringComparison.Ordinal)) count++;
        return count;
    }

    private sealed record BenchmarkCase(string Name, string Path, byte[]? Body = null);
    private sealed record MeasuredResponse(byte[] Body, double ElapsedMilliseconds, bool CacheHit, string ResponseHash);

    private sealed class SelectOnlyProbe : DbCommandInterceptor
    {
        private static void Check(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Benchmark fixtures permit SELECT commands only.");
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Check(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default) { Check(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) => throw new InvalidOperationException("Benchmark writes are prohibited.");
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default) => throw new InvalidOperationException("Benchmark writes are prohibited.");
    }
}

internal sealed class CacheBenchmarkReport
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public int ServerProcessId { get; set; }
    public DateTime ServerProcessStartedUtc { get; set; }
    public int Rounds { get; set; }
    public int RequestsPerBatch { get; set; }
    public string Notes { get; set; } = "";
    public List<CacheBenchmarkBatch> Batches { get; set; } = [];
}

internal sealed class CacheBenchmarkBatch
{
    public string Case { get; set; } = "";
    public int Round { get; set; }
    public string Mode { get; set; } = "";
    public CacheBenchmarkMiss? FirstMiss { get; set; }
    public double MeanMilliseconds { get; set; }
    public double MedianMilliseconds { get; set; }
    public double P95Milliseconds { get; set; }
    public double BatchWallMilliseconds { get; set; }
    public int CacheHits { get; set; }
    public double CpuMilliseconds { get; set; }
    public long? SqlCommands { get; set; }
    public CacheProcessSnapshot Before { get; set; } = null!;
    public CacheProcessSnapshot After { get; set; } = null!;
    public List<CacheBenchmarkSample> Samples { get; set; } = [];
}

internal sealed record CacheBenchmarkMiss(double LatencyMilliseconds, int ResponseBytes, string ResponseHash);
internal sealed record CacheBenchmarkSample(double LatencyMilliseconds, bool CacheHit, int ResponseBytes, string ResponseHash);
internal sealed record CacheProcessSnapshot(DateTimeOffset CapturedUtc, double CpuMilliseconds, long WorkingSetBytes, long PrivateBytes);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CacheBenchmarkReport))]
internal partial class CacheBenchmarkJsonContext : JsonSerializerContext;
