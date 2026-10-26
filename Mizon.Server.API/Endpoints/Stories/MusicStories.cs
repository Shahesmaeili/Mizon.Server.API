namespace Mizon.Server.API.Endpoints.Stories;

internal sealed class MusicStories : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGroup(ApiGroups.Stories).MapGet(nameof(MusicStories), Handle);

    private static Task<BaseApiResponse<StoriesResponse>> Handle([AsParameters] MusicStoriesRequest request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token)
        => executor.ExecuteAsync<MusicStoriesOperation, MusicStoriesRequest, StoriesResponse>(new() { Content = request }, metadata, token);
}
