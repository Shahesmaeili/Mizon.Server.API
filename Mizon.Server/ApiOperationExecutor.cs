using Microsoft.Extensions.DependencyInjection;

namespace Mizon.Server;

public class ApiOperationExecutor(IServiceProvider services) : IApiOperationExecutor
{
    public Task<BaseApiResponse<TResponse>> ExecuteAsync<TOperation, TRequest, TResponse>(
        BaseApiRequest<TRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
        where TOperation : class, IApiOperation<TRequest, TResponse>
        where TRequest : IApiRequest
        where TResponse : IApiResponse
    {
        var operation = services.GetRequiredService<TOperation>();

        return operation.HandleAsync(request, requestMetadata, cancellationToken);
    }
}
