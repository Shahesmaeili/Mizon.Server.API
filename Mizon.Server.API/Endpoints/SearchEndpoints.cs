using Mizon.Server.API.Endpoints.Search;

namespace Mizon.Server.API.Endpoints;

internal sealed class SearchEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new GlobalSearch().Map(app);
    }
}
