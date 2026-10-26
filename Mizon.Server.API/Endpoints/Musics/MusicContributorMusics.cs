namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicContributorMusics : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapGet(nameof(MusicContributorMusics), Handle);
    }

    private static Task<BaseApiResponse<MusicTabMusicsResponse>> Handle([AsParameters] MusicContributorMusicsRequest request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken)
        => executor.ExecuteAsync<MusicContributorMusicsOperation, MusicContributorMusicsRequest, MusicTabMusicsResponse>(new() { Content = request }, metadata, cancellationToken);
}
