namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicPlaylistDetails : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics)
            .MapPost(nameof(MusicPlaylistDetails), Handle);
    }

    private static Task<BaseApiResponse<MusicPlaylistDetailsResponse>> Handle(
        BaseApiRequest<MusicPlaylistDetailsRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicPlaylistDetailsOperation, MusicPlaylistDetailsRequest, MusicPlaylistDetailsResponse>(request, metadata, cancellationToken);
}

