namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicStemMix : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapGet(nameof(MusicStemMix), Handle);
    }

    private static Task<BaseApiResponse<MusicStemMixResponse>> Handle(
        [AsParameters] MusicStemMixRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        return apiOperationExecutor.ExecuteAsync<MusicStemMixOperation, MusicStemMixRequest, MusicStemMixResponse>(
            new() { Content = request }, requestMetadata, cancellationToken);
    }
}
