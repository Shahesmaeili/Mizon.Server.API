namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicGenreTypes : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicGenreTypes)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicGenreTypesResponse>> Handle(
        [AsParameters] MusicGenreTypesRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        var baseApiRequest = new BaseApiRequest<MusicGenreTypesRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicGenreTypesOperation, MusicGenreTypesRequest, MusicGenreTypesResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}
