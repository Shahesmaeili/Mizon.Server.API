namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicFile : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicFile)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicFileResponse>> Handle(
        [AsParameters] MusicFileRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicFileRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicFileOperation, MusicFileRequest, MusicFileResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}