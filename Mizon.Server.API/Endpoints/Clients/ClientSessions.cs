namespace Mizon.Server.API.Endpoints.Clients;

internal sealed class ClientSessions : IEndpoint
{
    public void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(ApiGroups.Clients).RequireAuthorization();
        group.MapGet("Sessions", ListAsync);
        group.MapPost("TerminateSession", TerminateAsync);
        group.MapPost("TerminateOtherSessions", TerminateOthersAsync);
        group.MapPost("UpdateSessionInactivity", UpdateInactivityAsync);
        group.MapPost("ConfirmSession", ConfirmAsync);
    }

    private static Task<BaseApiResponse<ClientSessionsResponse>> ListAsync(
        HttpContext context, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        return executor.ExecuteAsync<ClientSessionsOperation, ClientSessionsRequest, ClientSessionsResponse>(
            new() { Content = new() }, metadata, token);
    }

    private static Task<BaseApiResponse<TerminateSessionsResponse>> TerminateAsync(
        BaseApiRequest<TerminateSessionRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<TerminateSessionOperation, TerminateSessionRequest, TerminateSessionsResponse>(request, metadata, token);

    private static Task<BaseApiResponse<TerminateSessionsResponse>> TerminateOthersAsync(
        BaseApiRequest<TerminateOtherSessionsRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<TerminateOtherSessionsOperation, TerminateOtherSessionsRequest, TerminateSessionsResponse>(request, metadata, token);

    private static Task<BaseApiResponse<UpdateSessionInactivityResponse>> UpdateInactivityAsync(
        BaseApiRequest<UpdateSessionInactivityRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<UpdateSessionInactivityOperation, UpdateSessionInactivityRequest, UpdateSessionInactivityResponse>(
            request, metadata, token);

    private static Task<BaseApiResponse<ConfirmSessionResponse>> ConfirmAsync(
        BaseApiRequest<ConfirmSessionRequest> request, IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<ConfirmSessionOperation, ConfirmSessionRequest, ConfirmSessionResponse>(request, metadata, token);
}
