namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicPlaylistMembership : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics)
            .MapPost(nameof(MusicPlaylistMembership), Handle);
    }

    private static Task<BaseApiResponse<MusicPlaylistMembershipResponse>> Handle(
        BaseApiRequest<MusicPlaylistMembershipRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicPlaylistMembershipOperation, MusicPlaylistMembershipRequest, MusicPlaylistMembershipResponse>(request, metadata, cancellationToken);
}
