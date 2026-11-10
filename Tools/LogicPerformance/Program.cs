using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mizon.Database;
using Mizon.Server;
using Mizon.Server.Logic.Operations.Clients;
using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Operations.Musics;
using Mizon.Server.Logic.Operations.Search;
using Mizon.Server.Logic.Operations.Stories;
using Mizon.Server.Logic.Requests.Search;
using Mizon.Server.Logic.Responses.Clients;
using Mizon.Server.Logic.Responses.Medias;
using Mizon.Server.Logic.Responses.Musics;
using Mizon.Server.Logic.Responses.Search;
using Mizon.Server.Logic.Responses.Stories;

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "results");
var iterations = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30;
if (iterations < 1 || iterations > 1000) throw new ArgumentOutOfRangeException(nameof(iterations));
Directory.CreateDirectory(output);
var connection = Environment.GetEnvironmentVariable("MIZON_BENCHMARK_CONNECTION")
    ?? "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True";
var probe = new QueryProbe();
var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
    .UseSqlServer(connection).AddInterceptors(probe).Options;
await using var seed = new MizonDatabaseContext(options);
var client = await seed.ClientUsernames.AsNoTracking().Where(x => x.Username == "alish")
    .Where(x => x.IsActive).Select(x => new { x.ClientId, x.Client!.PublicId }).FirstAsync();
var metadata = new RequestMetadata { ClientId = client.ClientId, ClientPublicId = client.PublicId };
metadata.SessionPublicId = await seed.ClientSessions.AsNoTracking().Where(x => x.ClientId == client.ClientId)
    .OrderByDescending(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
var musicIds = await seed.Musics.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id)
    .Select(x => x.PublicId).Take(20).ToListAsync();
var musicId = musicIds.First();
var fileId = await seed.MusicFiles.AsNoTracking().OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
var lyricId = await seed.MusicLyrics.AsNoTracking().OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
var playlistId = await seed.MusicPlaylists.AsNoTracking().Where(x => x.ClientId == client.ClientId)
    .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
var storyId = await seed.Stories.AsNoTracking().Where(x => x.IsActive).Where(x => x.ExpireDateTime == null || x.ExpireDateTime > DateTimeOffset.UtcNow)
    .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
var mediaId = await seed.Medias.AsNoTracking().OrderBy(x => x.Id).Select(x => x.PublicId).FirstAsync();
var cases = new List<Case>();
cases.Add(Case.Create("Music", (MizonDatabaseContext db, CancellationToken ct) => new MusicOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("MusicFiles", (MizonDatabaseContext db, CancellationToken ct) => new MusicFileOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Stems", (MizonDatabaseContext db, CancellationToken ct) => new MusicStemMixOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Lyrics", (MizonDatabaseContext db, CancellationToken ct) => new MusicLyricOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Contributors", (MizonDatabaseContext db, CancellationToken ct) => new MusicContributorOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Comments", (MizonDatabaseContext db, CancellationToken ct) => new MusicCommentsOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Reactions", (MizonDatabaseContext db, CancellationToken ct) => new MusicReactionsOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Album", (MizonDatabaseContext db, CancellationToken ct) => new MusicAlbumMusicsOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("ContributorSongs", (MizonDatabaseContext db, CancellationToken ct) => new MusicContributorMusicsOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Genres", (MizonDatabaseContext db, CancellationToken ct) => new MusicGenreTypesOperation(db).HandleAsync(new() { Content = new() }, metadata, ct)));
cases.Add(Case.Create("ReactionTypes", (MizonDatabaseContext db, CancellationToken ct) => new MusicReactionTypesOperation(db).HandleAsync(new() { Content = new() }, metadata, ct)));
cases.Add(Case.Create("Library", (MizonDatabaseContext db, CancellationToken ct) => new MusicLibraryOperation(db).HandleAsync(new() { Content = new() { PageSize = 5 } }, metadata, ct)));
cases.Add(Case.Create("Library.EmptyPage", (MizonDatabaseContext db, CancellationToken ct) => new MusicLibraryOperation(db).HandleAsync(new() { Content = new() { Cursor = "0:1" } }, metadata, ct)));
cases.Add(Case.Create("Playlists", (MizonDatabaseContext db, CancellationToken ct) => new MusicPlaylistsOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Features", (MizonDatabaseContext db, CancellationToken ct) => new MusicFeaturesOperation(db).HandleAsync(new() { Content = new() { Limit = 12 } }, metadata, ct)));
cases.Add(Case.Create("Following.Music", (MizonDatabaseContext db, CancellationToken ct) => new FollowingActivitiesOperation(db).HandleAsync(new() { Content = new() { Scope = "music", PageSize = 4 } }, metadata, ct)));
cases.Add(Case.Create("Following.All", (MizonDatabaseContext db, CancellationToken ct) => new FollowingActivitiesOperation(db).HandleAsync(new() { Content = new() { Scope = "all", PageSize = 40 } }, metadata, ct)));
cases.Add(Case.Create("Following.EmptyPage", (MizonDatabaseContext db, CancellationToken ct) => new FollowingActivitiesOperation(db).HandleAsync(new() { Content = new() { Scope = "music", Cursor = "invalid" } }, metadata, ct)));
cases.Add(Case.Create("Profile", (MizonDatabaseContext db, CancellationToken ct) => new ClientProfileOperation(db).HandleAsync(new() { Content = new() { ClientPublicId = metadata.ClientPublicId!.Value } }, metadata, ct)));
cases.Add(Case.Create("Me", (MizonDatabaseContext db, CancellationToken ct) => new MeOperation(db).HandleAsync(new() { Content = new() }, metadata, ct)));
cases.Add(Case.Create("FindClient", (MizonDatabaseContext db, CancellationToken ct) => new FindClientIdOperation(db).HandleAsync(new() { Content = new() { ClientPublicId = metadata.ClientPublicId!.Value } }, metadata, ct)));
cases.Add(Case.Create("Moods", (MizonDatabaseContext db, CancellationToken ct) => new ClientMoodTypesOperation(db).HandleAsync(new() { Content = new() }, metadata, ct)));
cases.Add(Case.Create("Stories", (MizonDatabaseContext db, CancellationToken ct) => new StoriesOperation(db).HandleAsync(new() { Content = new() }, metadata, ct)));
cases.Add(Case.Create("MusicStories", (MizonDatabaseContext db, CancellationToken ct) => new MusicStoriesOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = musicId } }, metadata, ct)));
cases.Add(Case.Create("Stories.Missing", (MizonDatabaseContext db, CancellationToken ct) => new StoriesOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = Guid.Empty } }, metadata, ct)));
cases.Add(Case.Create("Music.Missing", (MizonDatabaseContext db, CancellationToken ct) => new MusicOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = Guid.Empty } }, metadata, ct)));
cases.Add(Case.Create("Stems.Missing", (MizonDatabaseContext db, CancellationToken ct) => new MusicStemMixOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = Guid.Empty } }, metadata, ct)));
cases.Add(Case.Create("FindClient.Missing", (MizonDatabaseContext db, CancellationToken ct) => new FindClientIdOperation(db).HandleAsync(new() { Content = new() { ClientPublicId = Guid.Empty } }, metadata, ct)));
if (fileId is Guid fid) cases.Add(Case.Create("Waveform", (MizonDatabaseContext db, CancellationToken ct) =>
    new MusicFileWaveformOperation(db).HandleAsync(new() { Content = new() { MusicFilePublicId = fid } }, metadata, ct)));
if (lyricId is Guid lid) cases.Add(Case.Create("LyricLines", (MizonDatabaseContext db, CancellationToken ct) =>
    new MusicLyricLineOperation(db).HandleAsync(new() { Content = new() { MusicLyricPublicId = lid } }, metadata, ct)));
if (playlistId is Guid pid) {
    cases.Add(Case.Create("PlaylistDetails", (MizonDatabaseContext db, CancellationToken ct) =>
        new MusicPlaylistDetailsOperation(db).HandleAsync(new() { Content = new() { MusicPlaylistPublicId = pid } }, metadata, ct)));
    cases.Add(Case.Create("PlaylistDetails.EmptyPage", (MizonDatabaseContext db, CancellationToken ct) =>
        new MusicPlaylistDetailsOperation(db).HandleAsync(new() { Content = new() { MusicPlaylistPublicId = pid, Cursor = "1" } }, metadata, ct)));
}
if (storyId is Guid sid) cases.Add(Case.Create("StoryDetails", (MizonDatabaseContext db, CancellationToken ct) =>
    new StoryDetailsOperation(db).HandleAsync(new() { Content = new() { StoryPublicId = sid } }, metadata, ct)));
cases.Add(Case.Create("Media", (MizonDatabaseContext db, CancellationToken ct) =>
    new MediaOperation(db).HandleAsync(new() { Content = new() { MediaPublicId = mediaId } }, metadata, ct)));
for (var i = 0; i < musicIds.Count; i++) {
    var id = musicIds[i];
    cases.Add(Case.Create($"Stems.All.{i}", (MizonDatabaseContext db, CancellationToken ct) =>
        new MusicStemMixOperation(db).HandleAsync(new() { Content = new() { MusicPublicId = id } }, metadata, ct)));
}
foreach (var query in new[] { "st", "alish", "ar", "آ", "خا", "no-match-mizon-7845", "%" })
    cases.Add(Case.Stream("Search." + Convert.ToHexString(Encoding.UTF8.GetBytes(query)),
        ct => new GlobalSearchOperation(options).SearchAsync(new() { Query = query }, ct)));

var report = new List<Measurement>();
foreach (var item in cases) {
    // Each operation gets a fresh context, like a request. Pool/SQL/query caches are warm.
    for (var warmup = 0; warmup < 3; warmup++) {
        await using var db = new MizonDatabaseContext(options);
        await item.Run(db, CancellationToken.None);
    }
    var elapsed = new List<double>(iterations);
    var allocations = new List<long>(iterations);
    var queries = new List<QuerySample>();
    var commands = new List<int>();
    var firstItems = new List<double>();
    for (var i = 0; i < iterations; i++) {
        await using var db = new MizonDatabaseContext(options);
        probe.Clear();
        var allocated = GC.GetTotalAllocatedBytes(false);
        var started = Stopwatch.GetTimestamp();
        await item.Run(db, CancellationToken.None);
        elapsed.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        allocations.Add(Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocated));
        var samples = probe.Snapshot();
        queries.AddRange(samples);
        commands.Add(samples.Length);
        if (item.FirstItemMs is double first) firstItems.Add(first);
        if (db.ChangeTracker.Entries().Any()) throw new InvalidOperationException(item.Name + " unexpectedly tracks entities");
    }
    // Serialize after timing; snapshots verify contracts/results, not just faster failures.
    var json = item.Snapshot();
    await File.WriteAllTextAsync(Path.Combine(output, item.Name + ".response.json"), json);
    var queryTimings = queries.Select(x => x.Milliseconds).ToList();
    report.Add(new Measurement(item.Name, iterations, Percentile(elapsed, .5), Percentile(elapsed, .95),
        commands.Min(), commands.Max(), Percentile(queryTimings, .5), Percentile(queryTimings, .95),
        queries.Count(x => x.Milliseconds < 1), queries.Count, Percentile(allocations.Select(x => (double)x).ToList(), .5),
        firstItems.Count == 0 ? null : Percentile(firstItems, .5), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))));
    var result = report[^1];
    Console.WriteLine($"{item.Name}: operation median={result.OperationMedianMs:F3}ms p95={result.OperationP95Ms:F3}ms; commands={result.MinCommands}-{result.MaxCommands}; reader-execute median={result.CommandMedianMs:F3}ms p95={result.CommandP95Ms:F3}ms");
    // Repeated command text only, never SQL parameter values/connection strings.
    foreach (var command in queries.DistinctBy(x => x.Hash))
        await File.WriteAllTextAsync(Path.Combine(output, command.Hash + ".sql"), command.Sql);
}
await File.WriteAllTextAsync(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(report, ProbeJson.Default.ListMeasurement));
Console.WriteLine($"Completed {cases.Count} read-only operation cases; no data changes.");
await CheckSearchLifetimeAsync(options);
if (args.Contains("--regressions"))
{
    await StoryCreateChecks.RunAsync(options);
    await FollowingActivityChecks.RunAsync(options);
}
if (args.Contains("--load"))
{
    foreach (var item in cases.Where(x => x.Name is "Music" or "Library.EmptyPage" or "Following.Music"))
    {
        var timings = new ConcurrentBag<double>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 80), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (_, ct) =>
        {
            // Each concurrent operation owns a different context. Response snapshots
            // are not inspected during this optional, read-only contention test.
            await using var database = new MizonDatabaseContext(options);
            var start = Stopwatch.GetTimestamp();
            await item.Run(database, ct);
            timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        });
        var ordered = timings.ToList();
        var line = FormattableString.Invariant($"{item.Name},8,80,{Percentile(ordered, .5):F3},{Percentile(ordered, .95):F3}");
        await File.AppendAllTextAsync(Path.Combine(output, "load.csv"), line + Environment.NewLine);
        Console.WriteLine("LOAD name,concurrency,requests,median_ms,p95_ms: " + line);
    }
}

static double Percentile(List<double> values, double percentile) {
    if (values.Count == 0) return 0;
    values.Sort();
    return values[Math.Clamp((int)Math.Ceiling(values.Count * percentile) - 1, 0, values.Count - 1)];
}

static async Task CheckSearchLifetimeAsync(DbContextOptions<MizonDatabaseContext> options) {
    var operation = new GlobalSearchOperation(options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await using (var reader = operation.SearchAsync(new() { Query = "st" }, timeout.Token).GetAsyncEnumerator())
        await reader.MoveNextAsync();
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try {
        await foreach (var _ in operation.SearchAsync(new() { Query = "st" }, cancelled.Token)) { }
        throw new InvalidOperationException("Pre-cancelled search unexpectedly completed.");
    } catch (OperationCanceledException) { }
    Console.WriteLine("PASS search early disposal and pre-cancelled enumeration.");
}

internal sealed class Case(string name, Func<MizonDatabaseContext, CancellationToken, Task> run, Func<string> snapshot) {
    public string Name => name;
    public Func<MizonDatabaseContext, CancellationToken, Task> Run => run;
    public Func<string> Snapshot => snapshot;
    public double? FirstItemMs { get; set; }

    public static Case Create<T>(string name, Func<MizonDatabaseContext, CancellationToken, Task<BaseApiResponse<T>>> action) where T : IApiResponse {
        BaseApiResponse<T>? response = null;
        return new Case(name, async (db, ct) => { response = await action(db, ct); },
            () => JsonSerializer.Serialize(response!, (JsonTypeInfo<BaseApiResponse<T>>)ProbeJson.Default.GetTypeInfo(typeof(BaseApiResponse<T>))!));
    }

    public static Case Stream(string name, Func<CancellationToken, IAsyncEnumerable<GlobalSearchItemResponse>> action) {
        List<GlobalSearchItemResponse> rows = [];
        Case? item = null;
        item = new Case(name, async (_, ct) => {
            rows.Clear();
            item!.FirstItemMs = null;
            var start = Stopwatch.GetTimestamp();
            await foreach (var row in action(ct)) {
                item.FirstItemMs ??= Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                rows.Add(row);
            }
        }, () => JsonSerializer.Serialize(rows.OrderBy(x => x.ItemType).ThenBy(x => x.PublicId).ToList(), ProbeJson.Default.ListGlobalSearchItemResponse));
        return item;
    }
}

internal sealed class QueryProbe : DbCommandInterceptor {
    private readonly ConcurrentQueue<QuerySample> _samples = new();
    public void Clear() { while (_samples.TryDequeue(out _)) { } }
    public QuerySample[] Snapshot() => _samples.ToArray();
    private static void EnsureReadOnly(DbCommand command) {
        if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The diagnostic runner permits SELECT commands only.");
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { EnsureReadOnly(command); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { EnsureReadOnly(command); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) => throw new InvalidOperationException("Writes prohibited.");
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default) => throw new InvalidOperationException("Writes prohibited.");
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData data, DbDataReader result) { Record(command, data); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default) { Record(command, data); return ValueTask.FromResult(result); }
    private void Record(DbCommand command, CommandExecutedEventData data) =>
        _samples.Enqueue(new QuerySample(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.CommandText)))[..16], command.CommandText, data.Duration.TotalMilliseconds));
}
internal sealed record QuerySample(string Hash, string Sql, double Milliseconds);
internal sealed record Measurement(string Name, int Iterations, double OperationMedianMs, double OperationP95Ms,
    int MinCommands, int MaxCommands, double CommandMedianMs, double CommandP95Ms, int CommandsUnderOneMs, int TotalCommands,
    double AllocatedBytesMedian, double? FirstItemMedianMs, string ResponseHash);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BaseApiResponse<MusicResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicFileResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicStemMixResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicLyricResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicContributorsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicCommentsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicReactionsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicTabMusicsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicGenreTypesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicReactionTypesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicLibraryResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicPlaylistsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicFeaturesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<FollowingActivitiesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<ClientProfileResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MeResponse>))]
[JsonSerializable(typeof(BaseApiResponse<FindClientIdResponse>))]
[JsonSerializable(typeof(BaseApiResponse<ClientMoodTypesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<StoriesResponse>))]
[JsonSerializable(typeof(BaseApiResponse<StoryResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicFileWaveformResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicLyricLineResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MusicPlaylistDetailsResponse>))]
[JsonSerializable(typeof(BaseApiResponse<MediaResponse>))]
[JsonSerializable(typeof(List<GlobalSearchItemResponse>))]
[JsonSerializable(typeof(List<Measurement>))]
internal partial class ProbeJson : JsonSerializerContext;

