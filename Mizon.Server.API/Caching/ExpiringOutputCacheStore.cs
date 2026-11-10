using Microsoft.AspNetCore.OutputCaching;

namespace Mizon.Server.API.Caching;

internal sealed class ExpiringOutputCacheStore(IOutputCacheStore inner, IHttpContextAccessor accessor, TimeProvider clock)
    : IOutputCacheStore, IDisposable
{
    private IOutputCacheStore? _store = inner;
    private IOutputCacheStore Store => Volatile.Read(ref _store) ?? throw new ObjectDisposedException(nameof(ExpiringOutputCacheStore));

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => Store.GetAsync(key, cancellationToken);

    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) => Store.EvictByTagAsync(tag, cancellationToken);

    public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        // The framework captures TTL at first write, but starts storage lifetime
        // after the whole response. Slow serialization must not extend a story's
        // expiry or delay the next scheduled carousel item.
        if (accessor.HttpContext?.Features.Get<ResponseCacheOutcome>()?.ExpiresAt is DateTimeOffset expires)
        {
            var remaining = expires - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) return ValueTask.CompletedTask;
            if (remaining < validFor) validFor = remaining;
        }
        return Store.SetAsync(key, value, tags, validFor, cancellationToken);
    }

    public void Dispose()
    {
        // The framework memory store itself need not implement IDisposable;
        // release our strong reference as well as disposing stores that do.
        if (Interlocked.Exchange(ref _store, null) is IDisposable disposable) disposable.Dispose();
    }
}
