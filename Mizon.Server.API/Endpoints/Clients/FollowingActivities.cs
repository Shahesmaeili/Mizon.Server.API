namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class FollowingActivities : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder) => endpointRouteBuilder
        .MapGroup(ApiGroups.Clients)
        .MapPost(nameof(FollowingActivities), Handle);

    private static Task<BaseApiResponse<FollowingActivitiesResponse>> Handle(
        BaseApiRequest<FollowingActivitiesRequest> request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) => executor
        .ExecuteAsync<FollowingActivitiesOperation, FollowingActivitiesRequest, FollowingActivitiesResponse>(
            request, metadata, cancellationToken);
}
