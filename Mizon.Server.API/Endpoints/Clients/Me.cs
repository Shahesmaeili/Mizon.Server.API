namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class Me : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Clients);

        routeGroupBuilder
            .MapGet($"{nameof(Me)}", Handle);
    }

    private static Task<BaseApiResponse<MeResponse>> Handle(
        [AsParameters] MeRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MeRequest>()
        {
            Content = request,
        };

        return apiOperationExecutor.ExecuteAsync<MeOperation, MeRequest, MeResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}
