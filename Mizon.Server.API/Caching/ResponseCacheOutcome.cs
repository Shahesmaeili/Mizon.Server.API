using Microsoft.AspNetCore.OutputCaching;

namespace Mizon.Server.API.Caching;

public sealed class ResponseCacheOutcome
{
    public bool IsSuccess { get; private init; }
    public DateTimeOffset? ExpiresAt { get; private init; }
    public bool SkipCache { get; private init; }

    public static void Set(HttpContext http, bool isSuccess, DateTimeOffset? expiresAt = null, bool skipCache = false)
    {
        http.Features.Set(new ResponseCacheOutcome { IsSuccess = isSuccess, ExpiresAt = expiresAt, SkipCache = skipCache });
        if (http.Features.Get<ResponseCacheRequestState>() is not { } state) return;
        var cache = state.Context;
        if (!isSuccess || skipCache)
        {
            cache.AllowCacheStorage = false;
            return;
        }
        // OutputCache freezes the lifetime on the FIRST body write, before its
        // ServeResponse callback. Set this while the typed operation returns.
        if (expiresAt is DateTimeOffset expires)
        {
            var remaining = expires - state.Clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) cache.AllowCacheStorage = false;
            else if (remaining < cache.ResponseExpirationTimeSpan) cache.ResponseExpirationTimeSpan = remaining;
        }
    }
}

internal sealed record ResponseCacheRequestState(OutputCacheContext Context, ResponseCacheRule Rule, long[] Versions, TimeProvider Clock);
