namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicReactionTypes : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicReactionTypes)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicReactionTypesResponse>> Handle(
        [AsParameters] MusicReactionTypesRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicReactionTypesRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicReactionTypesOperation, MusicReactionTypesRequest, MusicReactionTypesResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}