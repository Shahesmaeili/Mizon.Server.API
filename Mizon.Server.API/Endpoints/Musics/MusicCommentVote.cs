namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicCommentVote : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGroup(ApiGroups.Musics).MapPost(nameof(MusicCommentVote), Handle);

    private static Task<BaseApiResponse<MusicCommentVoteResponse>> Handle(
        BaseApiRequest<MusicCommentVoteRequest> request, IApiOperationExecutor executor,
        RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<MusicCommentVoteOperation, MusicCommentVoteRequest, MusicCommentVoteResponse>(
            request, metadata, token);
}
