using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Mizon.Server;

namespace Mizon.Server.API.Caching;

public sealed class MizonResponseCachePolicy(ResponseCacheVersions versions, IOptions<MizonResponseCacheOptions> options, TimeProvider clock) : IOutputCachePolicy
{
    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        context.EnableOutputCaching = false;
        context.AllowCacheLookup = false;
        context.AllowCacheStorage = false;
        var http = context.HttpContext;
        var request = http.Request;
        var rule = ResponseCacheCatalog.GetRule(http);
        if (!options.Value.Enabled || rule is null || http.User.Identity?.IsAuthenticated != true) return;
        var metadata = http.RequestServices.GetRequiredService<RequestMetadata>();
        if (metadata.ClientId is null || metadata.ClientPublicId is null || metadata.SessionPublicId is null) return;
        if (request.Headers.ContainsKey("Range") || request.QueryString.Value?.Length > 16384) return;
        CacheControlHeaderValue? directives = null;
        if (request.Headers.TryGetValue("Cache-Control", out var cacheControl)
            && !CacheControlHeaderValue.TryParse(cacheControl.ToString(), out directives)) return;
        if (directives?.NoStore == true) return;

        if (HttpMethods.IsPost(request.Method))
        {
            var bodyHash = await HashRequestBodyAsync(request, options.Value.MaximumRequestBodyBytes, cancellationToken);
            if (bodyHash is null) return;
            context.CacheVaryByRules.VaryByValues["body"] = bodyHash;
        }
        else if (request.ContentLength > 0) return;

        // Hash the exact query (including duplicate parameter ordering). Auth
        // query tokens are never stored in plain text in the cache key.
        context.CacheVaryByRules.QueryKeys = StringValues.Empty;
        context.CacheVaryByRules.VaryByValues["query"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.QueryString.Value ?? "")));
        context.CacheVaryByRules.HeaderNames = new StringValues([
            "Accept", "Accept-Encoding", "Accept-Language", "Content-Type", "Origin",
            "Mizon-Device-Identifier", "Mizon-Device-Model", "Mizon-Device-Name",
            "Mizon-Device-Username", "Mizon-Platform-Version", "Mizon-OS-Version"]);
        if (!rule.Shared)
            context.CacheVaryByRules.VaryByValues["identity"] = $"{metadata.ClientPublicId:N}:{metadata.SessionPublicId:N}";
        var snapshot = versions.Capture(rule.Domains);
        for (var index = 0; index < rule.Domains.Length; index++)
        {
            var tag = ResponseCacheVersions.Tag(rule.Domains[index], snapshot[index]);
            context.Tags.Add(tag);
            context.CacheVaryByRules.VaryByValues[$"domain-{index}"] = tag;
        }
        context.CacheVaryByRules.CacheKeyPrefix = "mizon-response-v1";
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = directives?.NoCache != true && directives?.MaxAge != TimeSpan.Zero;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = rule.Duration;
        http.Features.Set(new ResponseCacheRequestState(context, rule, snapshot, clock));
        // Caching is server-only. Browsers/proxies must not retain personal data.
        http.Response.Headers.CacheControl = "private, no-store";
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var state = http.Features.Get<ResponseCacheRequestState>();
        var outcome = http.Features.Get<ResponseCacheOutcome>();
        if (outcome is not { IsSuccess: true, SkipCache: false }
            || http.Response.StatusCode != StatusCodes.Status200OK
            || http.Response.Headers.ContainsKey("Set-Cookie")
            || http.RequestAborted.IsCancellationRequested
            || state is null || !versions.IsCurrent(state.Rule.Domains, state.Versions)
            || outcome.ExpiresAt <= clock.GetUtcNow())
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }

    private static async ValueTask<string?> HashRequestBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        // Unknown/oversized bodies continue normally, just without caching.
        if (request.ContentLength is not long length || length < 0 || length > limit) return null;
        request.EnableBuffering(bufferThreshold: limit + 1);
        var position = request.Body.Position;
        var buffer = ArrayPool<byte>.Shared.Rent((int)length + 1);
        try
        {
            var read = await request.Body.ReadAtLeastAsync(buffer.AsMemory(0, (int)length + 1), (int)length + 1, false, cancellationToken);
            return read == length ? Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read))) : null;
        }
        finally
        {
            request.Body.Position = position;
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
