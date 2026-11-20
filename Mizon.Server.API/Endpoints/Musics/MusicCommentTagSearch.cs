namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicCommentTagSearch : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGroup(ApiGroups.Musics).MapGet(nameof(MusicCommentTagSearch), Handle);

    private static Task<BaseApiResponse<MusicCommentTagSearchResponse>> Handle(
        [AsParameters] MusicCommentTagSearchRequest request, IApiOperationExecutor executor,
        RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<MusicCommentTagSearchOperation, MusicCommentTagSearchRequest, MusicCommentTagSearchResponse>(
            new() { Content = request }, metadata, token);
}
