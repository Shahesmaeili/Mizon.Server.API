namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicLyric : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicLyric)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicLyricResponse>> Handle(
        [AsParameters] MusicLyricRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicLyricRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicLyricOperation, MusicLyricRequest, MusicLyricResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}