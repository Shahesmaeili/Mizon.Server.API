using System.Globalization;
using Microsoft.AspNetCore.OutputCaching;

namespace Mizon.Server.API.Caching;

public sealed class ResponseCacheVersions(IOutputCacheStore store, ILogger<ResponseCacheVersions> logger)
{
    // Fixed domain counters: no unbounded dictionary of client IDs, locks or tokens.
    private readonly long[] _versions = new long[11];

    internal long[] Capture(ResponseCacheDomain[] domains) => domains.Select(domain => Volatile.Read(ref _versions[(int)domain])).ToArray();

    internal bool IsCurrent(ResponseCacheDomain[] domains, long[] snapshot)
    {
        for (var index = 0; index < domains.Length; index++)
            if (Volatile.Read(ref _versions[(int)domains[index]]) != snapshot[index]) return false;
        return true;
    }

    internal static string Tag(ResponseCacheDomain domain, long version) =>
        string.Create(CultureInfo.InvariantCulture, $"mizon:{(int)domain}:{version}");

    public async ValueTask InvalidateAsync(params ResponseCacheDomain[] domains)
    {
        // Change every key generation BEFORE eviction. A read already in flight
        // can never repopulate the generation used by subsequent requests.
        var expiredTags = new string[domains.Length];
        for (var index = 0; index < domains.Length; index++)
        {
            var previous = Interlocked.Increment(ref _versions[(int)domains[index]]) - 1;
            expiredTags[index] = Tag(domains[index], previous);
        }
        foreach (var tag in expiredTags)
        {
            try
            {
                // Committed writes must invalidate even if their caller disconnected.
                await store.EvictByTagAsync(tag, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // New reads already use the new generation; old entries age out.
                logger.LogWarning(exception, "Could not evict obsolete response cache tag {Tag}", tag);
            }
        }
    }
}
