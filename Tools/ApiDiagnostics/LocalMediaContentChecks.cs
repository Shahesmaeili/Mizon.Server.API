using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mizon.Server;
using Mizon.Server.API;
using Mizon.Server.API.Endpoints;
using Mizon.Server.API.JsonSerializerContexts;
using Mizon.Server.API.Services;
using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;

internal static class LocalMediaContentChecks
{
    public static async Task RunAsync()
    {
        // Place the synthetic files at a physical path, including hosts that symlink the system temp directory.
        var tempParent = new DirectoryInfo(Path.GetTempPath());
        var ancestors = new Stack<DirectoryInfo>();
        for (DirectoryInfo? directory = tempParent; directory is not null; directory = directory.Parent)
            ancestors.Push(directory);
        var parent = ancestors.Pop().FullName;
        while (ancestors.TryPop(out var ancestor))
        {
            var directory = new DirectoryInfo(Path.Combine(parent, ancestor.Name));
            parent = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        var temp = Directory.CreateDirectory(Path.Combine(parent, "mizon-media-" + Guid.NewGuid()));
        try
        {
            var root = temp.CreateSubdirectory("assets");
            var sibling = temp.CreateSubdirectory("assets-other");
            byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
            var content = Path.Combine(root.FullName, "source.tgs");
            var outside = Path.Combine(sibling.FullName, "outside.tgs");
            var unsupported = Path.Combine(root.FullName, "unsupported.txt");
            await File.WriteAllBytesAsync(content, bytes);
            await File.WriteAllBytesAsync(outside, bytes);
            await File.WriteAllBytesAsync(unsupported, bytes);

            var valid = Register(new Uri(content).AbsoluteUri, bytes.Length);
            valid.FileHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            valid.ChunkSize = 4;
            valid.TotalChunks = 2;
            var publicCatalog = temp.CreateSubdirectory("public-manifests").FullName;
            var digests = bytes.Chunk(4).SelectMany(static piece => SHA256.HashData(piece)).ToArray();
            var manifestPath = Path.Combine(publicCatalog, valid.FileHash + ".4.sha256");
            await File.WriteAllBytesAsync(manifestPath, digests);
            var forbidden = new List<MediaResponse>
            {
                Register(new Uri(outside).AbsoluteUri, bytes.Length),
                Register(new Uri(unsupported).AbsoluteUri, bytes.Length),
                Register(new Uri(content).AbsoluteUri, bytes.Length + 1),
                Register(new Uri(Path.Combine(root.FullName, "missing.tgs")).AbsoluteUri, bytes.Length),
                Register("https://storage.example/remote.tgs", bytes.Length),
                Register("file://other-host/remote.tgs", bytes.Length),
            };
            var link = Path.Combine(root.FullName, "redirect.tgs");
            try
            {
                File.CreateSymbolicLink(link, outside);
                forbidden.Add(Register(new Uri(link).AbsoluteUri, bytes.Length));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Console.WriteLine("SKIP local media symbolic link fixture: process cannot create a link.");
            }

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Convert.ToBase64String(SHA256.HashData("Mizon local media regression fixture"u8)),
                ["Jwt:Issuer"] = "fixture-issuer",
                ["Jwt:Audience"] = "fixture-audience",
            });
            builder.Services.AddSingleton<JwtTokenService>();
            builder.Services.AddAuthentication("jwt")
                .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("jwt", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddScoped<RequestMetadata>();
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default));
            builder.Services.AddSingleton(new LocalMediaContentStore([root.FullName]));
            builder.Services.AddSingleton(new PublicPeerMediaManifestStore(publicCatalog));
            builder.Services.AddSingleton<IApiOperationExecutor>(new FixtureExecutor([valid, .. forbidden]));
            await using var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            MediaEndpoints.Map(app);
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var token = app.Services.GetRequiredService<JwtTokenService>().GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid());
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            using var anonymous = await SendAsync(valid.MediaPublicId, authorized: false);
            Require(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous request is denied");
            using var queryToken = await SendAsync(valid.MediaPublicId, authorized: false, query: $"&access_token={token}");
            Require(queryToken.StatusCode == HttpStatusCode.Unauthorized, "query token is denied");
            using var ranged = await SendAsync(valid.MediaPublicId, new RangeHeaderValue(2, 5), query: "&path=ignored");
            Require(ranged.StatusCode == HttpStatusCode.PartialContent, "registered file is range-readable");
            Require(ranged.Content.Headers.ContentRange?.ToString() == "bytes 2-5/8", "range header includes registered size");
            Require(ranged.Content.Headers.ContentType?.MediaType == "application/octet-stream", "TGS payload stays binary");
            Require((await ranged.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes[2..6]), "range body matches the fixture");
            Require(ranged.Headers.CacheControl is { Private: true, NoStore: true }, "response bypasses shared caches");

            using var outOfBounds = await SendAsync(valid.MediaPublicId, new RangeHeaderValue(8, null));
            Require(outOfBounds.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, "unsatisfiable range is rejected");
            using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, $"/Medias/Media?MediaPublicId={valid.MediaPublicId:D}");
            metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var metadataResponse = await http.SendAsync(metadataRequest);
            metadataResponse.EnsureSuccessStatusCode();
            using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsByteArrayAsync());
            Require(metadata.RootElement.GetProperty("content").GetProperty("storagePath").GetString() == valid.StoragePath,
                "legacy metadata retains its local storage URI");
            foreach (var id in forbidden.Select(item => item.MediaPublicId).Append(Guid.Empty).Append(Guid.NewGuid()))
            {
                using var denial = await SendAsync(id);
                Require(denial.StatusCode == HttpStatusCode.NotFound, "ineligible media is not exposed");
                Require((await denial.Content.ReadAsByteArrayAsync()).Length == 0, "ineligible media response hides its path");
            }

            using var peerAnonymous = await GetPeerManifestAsync(valid.MediaPublicId, authorized: false);
            Require(peerAnonymous.StatusCode == HttpStatusCode.Unauthorized, "peer manifest requires the origin authorization");
            using var peerManifest = await GetPeerManifestAsync(valid.MediaPublicId);
            Require(peerManifest.StatusCode == HttpStatusCode.OK && peerManifest.Headers.CacheControl is { Private: true, NoStore: true },
                "only explicitly registered public media get a manifest");
            using var peerJson = JsonDocument.Parse(await peerManifest.Content.ReadAsByteArrayAsync());
            var peerBody = peerJson.RootElement.GetProperty("content");
            Require(peerBody.GetProperty("fileHash").GetString() == valid.FileHash
                && peerBody.GetProperty("pieceSha256").GetBytesFromBase64().SequenceEqual(digests),
                "published pieces match immutable origin metadata");
            using var privateManifest = await GetPeerManifestAsync(forbidden[0].MediaPublicId);
            Require(privateManifest.StatusCode == HttpStatusCode.NotFound, "a media lookup does not grant peer sharing");
            await File.WriteAllBytesAsync(manifestPath, [0x10]);
            using var malformed = await GetPeerManifestAsync(valid.MediaPublicId);
            Require(malformed.StatusCode == HttpStatusCode.NotFound, "an incomplete manifest fails closed");

            Console.WriteLine("PASS local media: authenticated ranges, binary payload, restricted storage roots and opt-in peer manifests.");

            async Task<HttpResponseMessage> GetPeerManifestAsync(Guid id, bool authorized = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/Medias/PeerManifest?MediaPublicId={id:D}");
                if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return await http.SendAsync(request);
            }

            async Task<HttpResponseMessage> SendAsync(Guid id, RangeHeaderValue? range = null, bool authorized = true, string query = "")
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/Medias/MediaContent?MediaPublicId={id:D}{query}");
                request.Headers.Range = range;
                if (authorized)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return await http.SendAsync(request);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private static MediaResponse Register(string storagePath, long size) => new()
    {
        MediaPublicId = Guid.NewGuid(), StoragePath = storagePath, FileSize = size, FileName = "registered-fixture.tgs",
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Local media content: {message}");
    }

    private sealed class FixtureExecutor(IEnumerable<MediaResponse> fixture) : IApiOperationExecutor
    {
        private readonly Dictionary<Guid, MediaResponse> media = fixture.ToDictionary(f => f.MediaPublicId);

        public Task<BaseApiResponse<TResponse>> ExecuteAsync<TOperation, TRequest, TResponse>(
            BaseApiRequest<TRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
            where TOperation : class, IApiOperation<TRequest, TResponse>
            where TRequest : IApiRequest
            where TResponse : IApiResponse
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (typeof(TOperation) != typeof(MediaOperation) || request.Content is not MediaRequest query)
                throw new InvalidOperationException("Unexpected local media diagnostic operation.");

            var found = media.GetValueOrDefault(query.MediaPublicId);
            var response = new BaseApiResponse<MediaResponse> { Content = found };
            return Task.FromResult((BaseApiResponse<TResponse>)(object)response);
        }
    }
}
