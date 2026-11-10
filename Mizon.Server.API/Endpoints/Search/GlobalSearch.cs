using Mizon.Server.API.JsonSerializerContexts;

namespace Mizon.Server.API.Endpoints.Search;

internal sealed class GlobalSearch : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Search)
            .MapGet("Global", Handle);
    }

    private static Task Handle(
        [AsParameters] GlobalSearchRequest request,
        GlobalSearchOperation operation,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/x-ndjson";
        context.Response.Headers.CacheControl = "no-store";

        return NdjsonResponseWriter.WriteAsync(
            operation.SearchAsync(request, cancellationToken),
            context.Response.BodyWriter,
            MizonJsonContexts.Default.GlobalSearchItemResponse,
            cancellationToken);
    }
}
