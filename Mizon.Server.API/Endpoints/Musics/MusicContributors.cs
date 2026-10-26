namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicContributors : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Musics);

        routeGroupBuilder
            .MapGet($"{nameof(MusicContributors)}", Handle);
    }

    private static async Task<BaseApiResponse<MusicContributorsResponse>> Handle(
        [AsParameters] MusicContributorRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        var baseApiRequest = new BaseApiRequest<MusicContributorRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MusicContributorOperation, MusicContributorRequest, MusicContributorsResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}
