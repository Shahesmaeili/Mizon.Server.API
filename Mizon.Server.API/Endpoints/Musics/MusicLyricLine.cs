namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicLyricLine : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicLyricLine)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicLyricLineResponse>> Handle(
        [AsParameters] MusicLyricLineRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicLyricLineRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicLyricLineOperation, MusicLyricLineRequest, MusicLyricLineResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}