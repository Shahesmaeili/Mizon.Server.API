namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicView : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapPost($"{nameof(MusicView)}", Handle);
    }

    private static Task<BaseApiResponse<MusicViewResponse>> Handle(
        BaseApiRequest<MusicViewRequest> baseApiRequest,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        return apiOperationExecutor.ExecuteAsync<MusicViewOperation, MusicViewRequest, MusicViewResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}
