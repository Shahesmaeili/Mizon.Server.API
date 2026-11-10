namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicPlay : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder) =>
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapPost(nameof(MusicPlay), Handle);

    private static Task<BaseApiResponse<MusicPlayResponse>> Handle(
        BaseApiRequest<MusicPlayRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicPlayOperation, MusicPlayRequest, MusicPlayResponse>(request, metadata, cancellationToken);
}
