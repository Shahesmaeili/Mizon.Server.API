namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicComments : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapGet($"{nameof(MusicComments)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicCommentsResponse>> Handle(
        [AsParameters] MusicCommentsRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        return await apiOperationExecutor.ExecuteAsync<MusicCommentsOperation, MusicCommentsRequest, MusicCommentsResponse>(new() { Content = request }, requestMetadata, cancellationToken);
    }
}
