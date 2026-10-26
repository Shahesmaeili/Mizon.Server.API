namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class StoryLike : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapPost(nameof(StoryLike), Handle);
    private static Task<BaseApiResponse<StoryResponse>> Handle(BaseApiRequest<StoryLikeRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) => executor.ExecuteAsync<StoryLikeOperation, StoryLikeRequest, StoryResponse>(request, metadata, token);
}
