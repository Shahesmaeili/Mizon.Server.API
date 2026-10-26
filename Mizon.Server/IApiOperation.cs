namespace Mizon.Server;

public interface IApiOperation<TRequest, TResponse>
    where TRequest : IApiRequest
    where TResponse : IApiResponse
{
    Task<BaseApiResponse<TResponse>> HandleAsync(
        BaseApiRequest<TRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken);
}
