namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class ClientProfile : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) => app.MapGroup(ApiGroups.Clients)
        .MapGet(nameof(ClientProfile), Handle)
        .RequireAuthorization();

    private static Task<BaseApiResponse<ClientProfileResponse>> Handle(
        [AsParameters] ClientProfileRequest request,
        IApiOperationExecutor executor,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync<ClientProfileOperation, ClientProfileRequest, ClientProfileResponse>(
            new() { Content = request }, metadata, cancellationToken);
}
