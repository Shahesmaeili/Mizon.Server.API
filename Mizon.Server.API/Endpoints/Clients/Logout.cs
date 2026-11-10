namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class Logout : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Clients);

        routeGroupBuilder
            .MapPost($"{nameof(Logout)}", Handle);
    }

    private static Task<BaseApiResponse<LogoutResponse>> Handle(
        BaseApiRequest<LogoutRequest> baseApiRequest,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        return apiOperationExecutor.ExecuteAsync<LogoutOperation, LogoutRequest, LogoutResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}