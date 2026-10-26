using Microsoft.Extensions.DependencyInjection;

namespace Mizon.Server;

public static class ServerServiceCollectionExtensions
{
    public static IServiceCollection AddMizonServer(this IServiceCollection services)
    {
        services.AddScoped<IApiOperationExecutor, ApiOperationExecutor>();

        return services;
    }
}
