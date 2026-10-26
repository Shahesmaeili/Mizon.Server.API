namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicAlbumMusics : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapGet(nameof(MusicAlbumMusics), Handle);
    }

    private static Task<BaseApiResponse<MusicTabMusicsResponse>> Handle([AsParameters] MusicAlbumMusicsRequest request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken)
        => executor.ExecuteAsync<MusicAlbumMusicsOperation, MusicAlbumMusicsRequest, MusicTabMusicsResponse>(new() { Content = request }, metadata, cancellationToken);
}
