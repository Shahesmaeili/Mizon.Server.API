namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class ClientMood : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGroup(ApiGroups.Clients).MapPost(nameof(ClientMood), Handle);

    private static Task<BaseApiResponse<ClientMoodResponse>> Handle(
        BaseApiRequest<ClientMoodRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken) =>
        executor.ExecuteAsync<ClientMoodOperation, ClientMoodRequest, ClientMoodResponse>(request, metadata, cancellationToken);
}
