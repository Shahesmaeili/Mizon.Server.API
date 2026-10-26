namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicReactions : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicReactions)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicReactionsResponse>> Handle(
        [AsParameters] MusicReactionsRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicReactionsRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicReactionsOperation, MusicReactionsRequest, MusicReactionsResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}