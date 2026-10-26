namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class Music : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(Music)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicResponse>> Handle(
        [AsParameters] MusicRequest request, 
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicOperation, MusicRequest, MusicResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}