using Mizon.Server.API.Endpoints.Clients;

namespace Mizon.Server.API.Endpoints;

internal sealed class ClientEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new Login().Map(app);
        new Me().Map(app);
        new Logout().Map(app);

    }
}
