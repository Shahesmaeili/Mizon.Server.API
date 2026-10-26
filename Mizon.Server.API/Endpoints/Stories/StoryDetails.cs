namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class StoryDetails : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapGet(nameof(StoryDetails), Handle);

    private static Task<BaseApiResponse<StoryResponse>> Handle([AsParameters] StoryRequest request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token)
        => executor.ExecuteAsync<StoryDetailsOperation, StoryRequest, StoryResponse>(new() { Content = request }, metadata, token);
}
