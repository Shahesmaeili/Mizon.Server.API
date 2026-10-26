namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class StoryView : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapPost(nameof(StoryView), Handle);
    private static Task<BaseApiResponse<StoryResponse>> Handle(BaseApiRequest<StoryRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) => executor.ExecuteAsync<StoryViewOperation, StoryRequest, StoryResponse>(request, metadata, token);
}
