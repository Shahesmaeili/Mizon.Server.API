namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicFeatures : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGroup(ApiGroups.Musics).MapPost(nameof(MusicFeatures), Handle);

    private static Task<BaseApiResponse<MusicFeaturesResponse>> Handle(
        BaseApiRequest<MusicFeaturesRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken) =>
        executor.ExecuteAsync<MusicFeaturesOperation, MusicFeaturesRequest, MusicFeaturesResponse>(request, metadata, cancellationToken);
}
