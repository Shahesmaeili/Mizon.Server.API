namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicReaction : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapPost($"{nameof(MusicReaction)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicReactionResponse>> Handle(
        BaseApiRequest<MusicReactionRequest> baseApiRequest,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        return await apiOperationExecutor.ExecuteAsync<MusicReactionOperation, MusicReactionRequest, MusicReactionResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}