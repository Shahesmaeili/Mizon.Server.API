using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.API.Endpoints.Medias;

internal sealed class PeerManifest : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Medias).MapGet(nameof(PeerManifest), Handle)
            .RequireAuthorization();
    }

    private static async Task<IResult> Handle([AsParameters] MediaRequest request, IApiOperationExecutor executor,
        PublicPeerMediaManifestStore store, RequestMetadata metadata, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.ContainsKey("access_token") || context.Request.Query.ContainsKey("token"))
            return Results.Unauthorized();
        if (!store.IsEnabled || request.MediaPublicId == Guid.Empty) return Results.NotFound();
        var result = await executor.ExecuteAsync<MediaOperation, MediaRequest, MediaResponse>(
            new() { Content = request }, metadata, token);
        if (!result.IsSuccess || result.Content is not { } media) return Results.NotFound();
        var manifest = await store.ReadAsync(media, token);
        return manifest is null ? Results.NotFound() : Results.Ok(new BaseApiResponse<PeerMediaManifestResponse> { Content = manifest });
    }
}
