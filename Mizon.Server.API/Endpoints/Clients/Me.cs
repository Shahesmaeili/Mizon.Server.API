namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class Me : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Clients);

        routeGroupBuilder
            .MapGet($"{nameof(Me)}", Handle)
            .CacheOutput(options =>
            {
                options.Expire(TimeSpan.FromSeconds(30));

                // تمایز کش بر اساس کاربر
                options.VaryByValue((ctx) =>
                {
                    var userId = ctx.User.FindFirst("sub")?.Value;
                    return new KeyValuePair<string, string>("UserId", userId);
                });
            });
    }

    private static async Task<BaseApiResponse<MeResponse>> Handle(
        [AsParameters] MeRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {

        var baseApiRequest = new BaseApiRequest<MeRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MeOperation, MeRequest, MeResponse>(baseApiRequest, requestMetadata, cancellationToken);
    }
}