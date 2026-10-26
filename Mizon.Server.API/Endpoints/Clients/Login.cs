using Mizon.Server.API.Middlewares;

namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class Login : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Clients);

        routeGroupBuilder
            .MapPost($"{nameof(Login)}", Handle)
            .AllowAnonymous();
    }

    private static async Task<BaseApiResponse<LoginResponse>> Handle(
        BaseApiRequest<LoginRequest> baseApiRequest,
        IApiOperationExecutor apiOperationExecutor,
        JwtTokenService jwtTokenService,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        var response = await apiOperationExecutor.ExecuteAsync<LoginOperation, LoginRequest, LoginResponse>(baseApiRequest, requestMetadata, cancellationToken);

        if (response.IsSuccess && response.Content is not null)
        {
            response.Content.AccessToken =
                jwtTokenService.GenerateAccessToken(response.Content.ClientPublicId, response.Content.SessionPublicId);

            if (response.Content.ClientId > 0)
            {
                ClientContextMiddleware.Clients.TryAdd(response.Content.ClientPublicId, response.Content.ClientId.Value);

                response.Content.ClientId = null;
            }
        }

        return response;
    }
}
