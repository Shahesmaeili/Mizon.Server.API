using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mizon.Server;

namespace Mizon.Server.API.Caching;

public static class ResponseCacheServiceCollectionExtensions
{
    public static IServiceCollection AddMizonResponseCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MizonResponseCacheOptions>().Configure(options =>
        {
            options.Enabled = configuration.GetValue("ResponseCache:Enabled", true);
            options.SizeLimitBytes = configuration.GetValue("ResponseCache:SizeLimitBytes", options.SizeLimitBytes);
            options.MaximumBodySizeBytes = configuration.GetValue("ResponseCache:MaximumBodySizeBytes", options.MaximumBodySizeBytes);
            options.MaximumRequestBodyBytes = configuration.GetValue("ResponseCache:MaximumRequestBodyBytes", options.MaximumRequestBodyBytes);
        }).Validate(options => options.SizeLimitBytes > 0 && options.MaximumBodySizeBytes > 0
            && options.MaximumBodySizeBytes <= options.SizeLimitBytes
            && options.MaximumRequestBodyBytes is > 0 and <= 1024 * 1024, "Invalid response-cache limits.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ResponseCacheVersions>();
        services.AddSingleton<MizonResponseCachePolicy>();
        services.AddHttpContextAccessor();
        services.AddScoped<ApiOperationExecutor>();
        services.Replace(ServiceDescriptor.Scoped<IApiOperationExecutor, CachingApiOperationExecutor>());
        services.AddOutputCache(options => options.AddBasePolicy(builder => builder.AddPolicy<MizonResponseCachePolicy>(), excludeDefaultPolicy: true));
        // Wrap the framework's registered factory without reflecting over its
        // internal store type; retain its size limits, eviction and pooling.
        var store = services.Last(descriptor => descriptor.ServiceType == typeof(IOutputCacheStore));
        var storeFactory = store.ImplementationFactory
            ?? throw new InvalidOperationException("Expected the framework output-cache store factory.");
        services.Remove(store);
        services.AddSingleton<IOutputCacheStore>(provider => new ExpiringOutputCacheStore(
            (IOutputCacheStore)storeFactory(provider), provider.GetRequiredService<IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()));
        services.AddOptions<OutputCacheOptions>().Configure<IOptions<MizonResponseCacheOptions>>((output, cache) =>
        {
            output.SizeLimit = cache.Value.SizeLimitBytes;
            output.MaximumBodySize = cache.Value.MaximumBodySizeBytes;
        });
        return services;
    }
}
