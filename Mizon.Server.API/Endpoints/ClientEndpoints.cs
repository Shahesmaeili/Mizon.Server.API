using Mizon.Server.API.Endpoints.Clients;

namespace Mizon.Server.API.Endpoints;

internal sealed class ClientEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new Login().Map(app);
        new Register().Map(app);
        new Me().Map(app);
        new ClientMoodTypes().Map(app);
        new ClientMood().Map(app);
        new ClientProfile().Map(app);
        new FollowingActivities().Map(app);
        new Logout().Map(app);

    }
}
