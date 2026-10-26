using Mizon.Server.API.JsonSerializerContexts;
using System.Text.Json;

namespace Mizon.Server.API.Endpoints.Search;

internal sealed class GlobalSearch : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Search)
            .MapGet("Global", Handle);
    }

    private static async Task Handle(
        [AsParameters] GlobalSearchRequest request,
        GlobalSearchOperation operation,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/x-ndjson";
        context.Response.Headers.CacheControl = "no-store";

        await foreach (var item in operation.SearchAsync(request, cancellationToken))
        {
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                item,
                MizonJsonContexts.Default.GlobalSearchItemResponse,
                cancellationToken);
            await context.Response.WriteAsync("\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
