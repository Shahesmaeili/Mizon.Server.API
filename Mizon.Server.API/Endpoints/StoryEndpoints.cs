using Mizon.Server.API.Endpoints.Stories;

namespace Mizon.Server.API.Endpoints;

internal sealed class StoryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new StoriesEndpoint().Map(app);
        new MusicStories().Map(app);
        new StoryDetails().Map(app);
        new StoryView().Map(app);
        new StoryLike().Map(app);
        new StoryCreate().Map(app);
    }
}
