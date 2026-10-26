using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.API.Endpoints.Medias;

internal sealed class Media : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var routeGroupBuilder = endpointRouteBuilder.MapGroup(ApiGroups.Medias);

        routeGroupBuilder
            .MapGet($"{nameof(Media)}", HandleInfo);

    }

    private static async Task<BaseApiResponse<MediaResponse>> HandleInfo(
        [AsParameters] MediaRequest request,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata,
        CancellationToken cancellationToken)
    {
        var baseApiRequest = new BaseApiRequest<MediaRequest>()
        {
            Content = request,
        };

        return await apiOperationExecutor.ExecuteAsync<MediaOperation, MediaRequest, MediaResponse>(baseApiRequest, requestMetadata, cancellationToken);
     }

}
