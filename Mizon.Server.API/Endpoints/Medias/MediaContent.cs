using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.API.Endpoints.Medias;

internal sealed class MediaContent : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGroup(ApiGroups.Medias)
            .MapGet(nameof(MediaContent), HandleContent)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleContent(
        [AsParameters] MediaRequest request,
        IApiOperationExecutor apiOperationExecutor,
        LocalMediaContentStore store,
        RequestMetadata requestMetadata,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Query tokens are for SignalR. Binary media credentials stay in request headers.
        if (context.Request.Query.ContainsKey("access_token") || context.Request.Query.ContainsKey("token"))
            return Results.Unauthorized();
        if (request.MediaPublicId == Guid.Empty)
            return Results.NotFound();

        var response = await apiOperationExecutor.ExecuteAsync<MediaOperation, MediaRequest, MediaResponse>(
            new() { Content = request }, requestMetadata, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } media || store.TryOpen(media) is not { } content)
            return Results.NotFound();

        return Results.File(content.Stream, content.ContentType, enableRangeProcessing: true);
    }
}
