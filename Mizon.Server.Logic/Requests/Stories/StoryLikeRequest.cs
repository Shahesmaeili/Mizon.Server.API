namespace Mizon.Server.Logic.Requests.Stories;

public sealed class StoryLikeRequest : IApiRequest
{
    public Guid StoryPublicId { get; set; }
    public bool Liked { get; set; }
}
