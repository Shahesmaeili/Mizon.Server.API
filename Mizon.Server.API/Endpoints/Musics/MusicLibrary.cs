namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicLibrary : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics)
            .MapPost(nameof(MusicLibrary), Handle);
    }

    private static Task<BaseApiResponse<MusicLibraryResponse>> Handle(
        BaseApiRequest<MusicLibraryRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicLibraryOperation, MusicLibraryRequest, MusicLibraryResponse>(request, metadata, cancellationToken);
}

