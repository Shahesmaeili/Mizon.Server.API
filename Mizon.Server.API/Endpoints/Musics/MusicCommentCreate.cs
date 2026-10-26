namespace Mizon.Server.API.Endpoints.Musics;

internal sealed class MusicCommentCreate : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Musics).MapPost($"{nameof(MusicCommentCreate)}", Handle);
    }

    private static Task<BaseApiResponse<MusicCommentCreateResponse>> Handle(
        BaseApiRequest<MusicCommentCreateRequest> request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        return apiOperationExecutor.ExecuteAsync<MusicCommentCreateOperation, MusicCommentCreateRequest, MusicCommentCreateResponse>(request, requestMetadata, cancellationToken);
    }
}
