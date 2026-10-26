namespace Mizon.Server;

public interface IApiOperationExecutor
{
    Task<BaseApiResponse<TResponse>> ExecuteAsync<TOperation, TRequest, TResponse>(
        BaseApiRequest<TRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
        where TOperation : class, IApiOperation<TRequest, TResponse>
        where TRequest : IApiRequest
        where TResponse : IApiResponse;
}
