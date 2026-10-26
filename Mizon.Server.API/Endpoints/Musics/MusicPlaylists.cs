namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicPlaylists : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics)
            .MapPost(nameof(MusicPlaylists), Handle);
    }

    private static Task<BaseApiResponse<MusicPlaylistsResponse>> Handle(
        BaseApiRequest<MusicPlaylistsRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicPlaylistsOperation, MusicPlaylistsRequest, MusicPlaylistsResponse>(request, metadata, cancellationToken);
}
