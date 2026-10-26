namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicPlaylistCreate : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics)
            .MapPost(nameof(MusicPlaylistCreate), Handle);
    }

    private static Task<BaseApiResponse<MusicPlaylistCreateResponse>> Handle(
        BaseApiRequest<MusicPlaylistCreateRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicPlaylistCreateOperation, MusicPlaylistCreateRequest, MusicPlaylistCreateResponse>(request, metadata, cancellationToken);
}
