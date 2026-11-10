namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class ClientMoodTypes : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGroup(ApiGroups.Clients).MapGet(nameof(ClientMoodTypes), Handle);

    private static Task<BaseApiResponse<ClientMoodTypesResponse>> Handle(
        IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken cancellationToken) =>
        executor.ExecuteAsync<ClientMoodTypesOperation, ClientMoodTypesRequest, ClientMoodTypesResponse>(
            new() { Content = new() }, metadata, cancellationToken);
}
