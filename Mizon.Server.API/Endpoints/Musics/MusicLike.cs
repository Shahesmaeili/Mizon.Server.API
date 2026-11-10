namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicLike : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGroup(ApiGroups.Musics).MapPost(nameof(MusicLike), Handle);

    private static Task<BaseApiResponse<MusicLikeResponse>> Handle(
        BaseApiRequest<MusicLikeRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicLikeOperation, MusicLikeRequest, MusicLikeResponse>(request, metadata, cancellationToken);
}
