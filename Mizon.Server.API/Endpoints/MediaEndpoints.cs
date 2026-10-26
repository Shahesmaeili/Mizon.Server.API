using Mizon.Server.API.Endpoints.Medias;

namespace Mizon.Server.API.Endpoints;

public sealed class MediaEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new Media().Map(app);
    }
}