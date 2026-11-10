using System.Data.Common;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Mizon.Database;
using Mizon.Server;
using Mizon.Server.API.Services;
using Mizon.Server.Logic.Requests.Clients;
using Mizon.Server.Logic.Requests.Musics;
using Mizon.Server.Logic.Responses.Search;

internal static class HttpChecks
{
    public static async Task RunAsync(string baseUrl, string apiProjectDirectory)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        if (!baseUri.IsLoopback || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("HTTP diagnostics are restricted to a local HTTPS development host.");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.GetFullPath(apiProjectDirectory))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("MIZON_BENCHMARK_CONNECTION")
                ?? "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True")
            .AddInterceptors(new SelectOnlyGuard()).Options;
        await using var database = new MizonDatabaseContext(options);
        var client = await database.ClientUsernames.AsNoTracking()
            .Where(x => x.Username == "alish").Where(x => x.IsActive)
            .Select(x => new { x.ClientId, x.Client!.PublicId }).FirstOrDefaultAsync();
        if (client is null) throw new InvalidOperationException("HTTP diagnostics need the existing alish fixture.");
        var sessionId = await database.ClientSessions.AsNoTracking()
            .Where(x => x.ClientId == client.ClientId).Where(x => x.LogoutDateTime == null)
            .OrderByDescending(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
        if (sessionId is null) throw new InvalidOperationException("HTTP diagnostics need an existing active alish session; no session is created.");
        var musicId = await database.Musics.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
        if (musicId is null) throw new InvalidOperationException("HTTP diagnostics need an existing active music.");
        var fileId = await database.MusicFiles.AsNoTracking()
            .Where(x => x.Music!.PublicId == musicId.Value)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
        var lyricId = await database.MusicLyrics.AsNoTracking()
            .Where(x => x.Music!.PublicId == musicId.Value)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
        var mediaId = await database.Medias.AsNoTracking()
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();
        var playlistId = await database.MusicPlaylists.AsNoTracking().Where(x => x.ClientId == client.ClientId)
            .OrderBy(x => x.Id).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync();

        // Only the token generator reads the local JWT settings. Neither settings
        // nor generated tokens are written to output or sent outside loopback.
        var token = new JwtTokenService(configuration).GenerateAccessToken(client.PublicId, sessionId.Value);
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            // This diagnostic is loopback-only; the dev certificate may not be trusted.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        await ExpectStatusAsync("anonymous read", HttpStatusCode.Unauthorized,
            new HttpRequestMessage(HttpMethod.Get, "Clients/Me"));
        var malformedToken = new HttpRequestMessage(HttpMethod.Get, "Clients/Me");
        malformedToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        await ExpectStatusAsync("malformed bearer token", HttpStatusCode.Unauthorized, malformedToken);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await ExpectStatusAsync("authenticated unknown route", HttpStatusCode.NotFound,
            new HttpRequestMessage(HttpMethod.Get, "diagnostic-route-that-does-not-exist"));

        foreach (var version in new[] { HttpVersion.Version20, HttpVersion.Version30 })
        {
            // SendAsync uses the message's version, not HttpClient's default.
            // Exact verifies the actual HTTP/2 and QUIC listeners without fallback.
            using var request = new HttpRequestMessage(HttpMethod.Get, "Clients/Me")
            {
                Version = version,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            using var response = await http.SendAsync(request);
            Require(response.Version == version, $"exact HTTP/{version} negotiation");
            using var document = await ReadEnvelopeAsync(response, $"exact HTTP/{version} authenticated read");
        }

        await GetEnvelopeAsync("Me", "Clients/Me");
        await GetEnvelopeAsync("ClientProfile", $"Clients/ClientProfile?ClientPublicId={client.PublicId:D}");
        await GetEnvelopeAsync("ClientMoodTypes", "Clients/ClientMoodTypes");
        await GetEnvelopeAsync("Music", $"Musics/Music?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicContributors", $"Musics/MusicContributors?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicGenreTypes", "Musics/MusicGenreTypes");
        await GetEnvelopeAsync("MusicReactionTypes", "Musics/MusicReactionTypes");
        await GetEnvelopeAsync("MusicReactions", $"Musics/MusicReactions?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicComments", $"Musics/MusicComments?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicLyric", $"Musics/MusicLyric?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicAlbumMusics", $"Musics/MusicAlbumMusics?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicContributorMusics", $"Musics/MusicContributorMusics?MusicPublicId={musicId:D}");
        await GetEnvelopeAsync("MusicStemMix", $"Musics/MusicStemMix?MusicPublicId={musicId:D}");
        if (fileId is Guid file)
        {
            await GetEnvelopeAsync("MusicFile", $"Musics/MusicFile?MusicPublicId={musicId:D}");
            await GetEnvelopeAsync("Waveform", $"Musics/MusicFileWaveform?MusicFilePublicId={file:D}");
        }
        else Console.WriteLine("SKIP HTTP music file/waveform: no existing file fixture.");
        if (lyricId is Guid lyric)
            await GetEnvelopeAsync("MusicLyricLine", $"Musics/MusicLyricLine?MusicLyricPublicId={lyric:D}");
        else Console.WriteLine("SKIP HTTP lyric lines: no existing lyric fixture.");
        if (mediaId is Guid media)
            await GetEnvelopeAsync("Media metadata", $"Medias/Media?MediaPublicId={media:D}");

        await PostEnvelopeAsync("MusicLibrary", "Musics/MusicLibrary",
            JsonSerializer.SerializeToUtf8Bytes(new BaseApiRequest<MusicLibraryRequest> { Content = new() { PageSize = 5 } },
                HttpCheckJsonContext.Default.BaseApiRequestMusicLibraryRequest));
        await PostEnvelopeAsync("MusicPlaylists", "Musics/MusicPlaylists",
            JsonSerializer.SerializeToUtf8Bytes(new BaseApiRequest<MusicPlaylistsRequest> { Content = new() { MusicPublicId = musicId } },
                HttpCheckJsonContext.Default.BaseApiRequestMusicPlaylistsRequest));
        await PostEnvelopeAsync("MusicFeatures", "Musics/MusicFeatures",
            JsonSerializer.SerializeToUtf8Bytes(new BaseApiRequest<MusicFeaturesRequest> { Content = new() { Limit = 3 } },
                HttpCheckJsonContext.Default.BaseApiRequestMusicFeaturesRequest));
        await PostEnvelopeAsync("FollowingActivities", "Clients/FollowingActivities",
            JsonSerializer.SerializeToUtf8Bytes(new BaseApiRequest<FollowingActivitiesRequest> { Content = new() { Scope = "music", PageSize = 4 } },
                HttpCheckJsonContext.Default.BaseApiRequestFollowingActivitiesRequest));
        if (playlistId is Guid playlist)
            await PostEnvelopeAsync("MusicPlaylistDetails", "Musics/MusicPlaylistDetails",
                JsonSerializer.SerializeToUtf8Bytes(new BaseApiRequest<MusicPlaylistDetailsRequest>
                {
                    Content = new() { MusicPlaylistPublicId = playlist, PageSize = 10 }
                }, HttpCheckJsonContext.Default.BaseApiRequestMusicPlaylistDetailsRequest));
        else Console.WriteLine("SKIP HTTP playlist detail: no existing owned playlist.");

        using (var request = new HttpRequestMessage(HttpMethod.Get, "Stories/Stories"))
        using (var response = await http.SendAsync(request))
        using (var document = await ReadEnvelopeAsync(response, "Stories"))
        {
            var stories = Property(Property(document.RootElement, "Content"), "Stories");
            if (stories.GetArrayLength() > 0)
                await GetEnvelopeAsync("StoryDetails",
                    $"Stories/StoryDetails?StoryPublicId={Property(stories[0], "PublicId").GetGuid():D}");
            else Console.WriteLine("SKIP HTTP story detail: no currently visible story.");
        }
        await GetEnvelopeAsync("MusicStories", $"Stories/MusicStories?MusicPublicId={musicId:D}");

        // Invalid compressed JSON targets a read-only operation, never a write endpoint.
        await ExpectStatusAsync("malformed JSON", HttpStatusCode.BadRequest,
            new HttpRequestMessage(HttpMethod.Post, "Musics/MusicLibrary")
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json")
            });
        byte[] compressedInvalid;
        using (var bytes = new MemoryStream())
        {
            using (var compressor = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true))
                compressor.Write("{"u8);
            compressedInvalid = bytes.ToArray();
        }
        var compressed = new ByteArrayContent(compressedInvalid);
        compressed.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        compressed.Headers.ContentEncoding.Add("gzip");
        await ExpectStatusAsync("gzip request decompression", HttpStatusCode.BadRequest,
            new HttpRequestMessage(HttpMethod.Post, "Musics/MusicLibrary") { Content = compressed });

        using (var request = new HttpRequestMessage(HttpMethod.Get, "Clients/ClientMoodTypes"))
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Require(response.IsSuccessStatusCode && response.Content.Headers.ContentEncoding.Contains("gzip"),
                "gzip response compression");
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var decompressor = new GZipStream(stream, CompressionMode.Decompress);
            using var document = await JsonDocument.ParseAsync(decompressor);
            Require(Property(document.RootElement, "IsSuccess").GetBoolean(), "compressed envelope round trip");
            Console.WriteLine("PASS HTTP gzip response compression and JSON round trip.");
        }

        await CheckSearchAsync(http, "alish", stopAfterFirst: false);
        await CheckSearchAsync(http, "st", stopAfterFirst: true);
        await CheckSearchAsync(http, "mizon-diagnostic-no-result-" + Guid.NewGuid().ToString("N"),
            stopAfterFirst: false, expectEmpty: true);
        // A fresh read after aborting a streaming response checks that the host
        // remains responsive and the next response is not contaminated.
        await GetEnvelopeAsync("read after search cancellation", "Musics/MusicReactionTypes");

        using (var request = new HttpRequestMessage(HttpMethod.Get, "openapi/v1.json"))
        using (var response = await http.SendAsync(request))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                Console.WriteLine("SKIP HTTP OpenAPI: not enabled for this environment.");
            else
            {
                Require(response.IsSuccessStatusCode, "OpenAPI status");
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                Require(document.RootElement.GetProperty("paths").TryGetProperty("/Search/Global", out _),
                    "OpenAPI search route");
                Console.WriteLine("PASS HTTP OpenAPI document and search route.");
            }
        }
        await SocketChecks.RunAsync(baseUri, token, client.PublicId, musicId.Value);
        Console.WriteLine("PASS HTTP transport smoke completed; no write endpoints were called.");

        async Task ExpectStatusAsync(string label, HttpStatusCode status, HttpRequestMessage request)
        {
            using (request)
            using (var response = await http.SendAsync(request))
            {
                Require(response.StatusCode == status, $"{label}: expected {(int)status}, received {(int)response.StatusCode}");
                Console.WriteLine($"PASS HTTP {label}: {(int)status}.");
            }
        }
        async Task GetEnvelopeAsync(string label, string route)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            using var response = await http.SendAsync(request);
            using var document = await ReadEnvelopeAsync(response, label);
        }
        async Task PostEnvelopeAsync(string label, string route, byte[] json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new ByteArrayContent(json)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.SendAsync(request);
            using var document = await ReadEnvelopeAsync(response, label);
        }
    }

    private static async Task<JsonDocument> ReadEnvelopeAsync(HttpResponseMessage response, string label)
    {
        Require(response.IsSuccessStatusCode, $"{label}: HTTP {(int)response.StatusCode}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        var document = await JsonDocument.ParseAsync(stream);
        try
        {
            Require(Property(document.RootElement, "IsSuccess").GetBoolean(), label + ": operation failed");
            Require(Property(document.RootElement, "Content").ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined),
                label + ": missing content");
            Console.WriteLine($"PASS HTTP {label}: successful JSON envelope over HTTP/{response.Version}.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static async Task CheckSearchAsync(HttpClient http, string query, bool stopAfterFirst, bool expectEmpty = false)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "Search/Global?Query=" + Uri.EscapeDataString(query));
        var start = Stopwatch.GetTimestamp();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        Require(response.IsSuccessStatusCode, "search response status");
        Require(response.Content.Headers.ContentType?.MediaType == "application/x-ndjson", "search NDJSON content type");
        Require(response.Headers.CacheControl?.NoStore == true, "search no-store policy");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        var count = 0;
        var firstMs = 0d;
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable(stream,
            HttpCheckJsonContext.Default.GlobalSearchItemResponse, topLevelValues: true, cancellationToken: cancellation.Token))
        {
            Require(item is not null && item.PublicId != Guid.Empty && item.ItemType is "Music" or "Client", "valid streamed DTO");
            count++;
            if (count == 1) firstMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (stopAfterFirst)
            {
                cancellation.Cancel();
                break;
            }
        }
        if (expectEmpty)
        {
            Require(count == 0, "empty search has no result frames");
            Console.WriteLine("PASS HTTP empty NDJSON search completes with a successful, zero-item response.");
        }
        else if (stopAfterFirst && count == 0)
            Console.WriteLine("SKIP HTTP search cancellation after item: 'st' fixture currently has no results.");
        else
        {
            Require(count > 0, "search returns the existing fixture");
            Console.WriteLine(stopAfterFirst
                ? $"PASS HTTP search first item in {firstMs:F2} ms; response cancelled/disposed immediately."
                : $"PASS HTTP source-generated NDJSON stream: {count} result(s), first item in {firstMs:F2} ms.");
        }
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new InvalidOperationException("Expected response property: " + name);
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("HTTP smoke: " + label);
    }

    private sealed class SelectOnlyGuard : DbCommandInterceptor
    {
        private static void Check(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("HTTP fixture discovery permits SELECT only.");
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Check(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
            => throw new InvalidOperationException("Database writes prohibited.");
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Database writes prohibited.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BaseApiRequest<MusicLibraryRequest>))]
[JsonSerializable(typeof(BaseApiRequest<MusicPlaylistsRequest>))]
[JsonSerializable(typeof(BaseApiRequest<MusicPlaylistDetailsRequest>))]
[JsonSerializable(typeof(BaseApiRequest<MusicFeaturesRequest>))]
[JsonSerializable(typeof(BaseApiRequest<FollowingActivitiesRequest>))]
[JsonSerializable(typeof(GlobalSearchItemResponse))]
internal partial class HttpCheckJsonContext : JsonSerializerContext;

