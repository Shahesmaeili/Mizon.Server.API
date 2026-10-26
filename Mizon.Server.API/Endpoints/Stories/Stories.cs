namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class StoriesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapGet("Stories", Handle);
    private static Task<BaseApiResponse<StoriesResponse>> Handle([AsParameters] StoriesRequest request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) => executor.ExecuteAsync<StoriesOperation, StoriesRequest, StoriesResponse>(new() { Content = request }, metadata, token);
}
