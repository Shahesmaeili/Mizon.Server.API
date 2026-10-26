namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicFileWaveform : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicFileWaveform)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicFileWaveformResponse>> Handle(
        [AsParameters] MusicFileWaveformRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MusicFileWaveformRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicFileWaveformOperation, MusicFileWaveformRequest, MusicFileWaveformResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}