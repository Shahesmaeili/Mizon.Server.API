using Mizon.Server.API.Middlewares;

namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class Register : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Clients)
            .MapPost($"{nameof(Register)}", Handle)
            .AllowAnonymous();
    }

    private static async Task<BaseApiResponse<RegisterResponse>> Handle(
        BaseApiRequest<RegisterRequest> baseApiRequest,
        IApiOperationExecutor apiOperationExecutor,
        JwtTokenService jwtTokenService,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        var response = await apiOperationExecutor.ExecuteAsync<RegisterOperation, RegisterRequest, RegisterResponse>(
            baseApiRequest, requestMetadata, cancellationToken);

        if (response.IsSuccess && response.Content is not null)
        {
            response.Content.AccessToken = jwtTokenService.GenerateAccessToken(
                response.Content.ClientPublicId, response.Content.SessionPublicId);

            if (response.Content.ClientId > 0)
                ClientContextMiddleware.Clients.TryAdd(response.Content.ClientPublicId, response.Content.ClientId.Value);

            response.Content.ClientId = null;
        }

        return response;
    }
}
