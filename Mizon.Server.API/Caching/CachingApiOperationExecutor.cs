using Mizon.Server;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.API.Caching;

public sealed class CachingApiOperationExecutor(ApiOperationExecutor inner, IHttpContextAccessor accessor, ResponseCacheVersions versions) : IApiOperationExecutor
{
    public async Task<BaseApiResponse<TResponse>> ExecuteAsync<TOperation, TRequest, TResponse>(
        BaseApiRequest<TRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken)
        where TOperation : class, IApiOperation<TRequest, TResponse>
        where TRequest : IApiRequest
        where TResponse : IApiResponse
    {
        var invalidations = ResponseCacheCatalog.GetInvalidatedDomains(typeof(TOperation));
        try
        {
            var response = await inner.ExecuteAsync<TOperation, TRequest, TResponse>(request, metadata, cancellationToken);
            if (accessor.HttpContext is { } http && ResponseCacheCatalog.GetRule(http)?.OperationType == typeof(TOperation))
            {
                var path = response.Content switch
                {
                    MediaResponse media => media.StoragePath,
                    MusicFileResponse file => file.StoragePath,
                    _ => null
                };
                // Presigned links have independent expiration. Without explicit
                // expiry metadata it is safer to bypass their response cache.
                var temporaryUrl = Uri.TryCreate(path, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "https" || uri.Scheme == "http") && uri.Query.Length != 0;
                ResponseCacheOutcome.Set(http, response.IsSuccess && response.Content is not null, response.CacheExpiresAt, temporaryUrl);
            }
            return response;
        }
        finally
        {
            // Also cover a write committed before a subsequent read/serialization
            // error. Invalid attempts may evict unnecessarily but never serve stale data.
            if (invalidations is not null) await versions.InvalidateAsync(invalidations);
        }
    }
}
