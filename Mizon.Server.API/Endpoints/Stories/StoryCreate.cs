namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class StoryCreate : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapPost(nameof(StoryCreate), Handle);
    private static Task<BaseApiResponse<StoryResponse>> Handle(BaseApiRequest<StoryCreateRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) => executor.ExecuteAsync<StoryCreateOperation, StoryCreateRequest, StoryResponse>(request, metadata, token);
}
