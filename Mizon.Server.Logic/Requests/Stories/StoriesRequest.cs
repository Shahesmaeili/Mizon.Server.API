namespace Mizon.Server.Logic.Requests.Stories;

public sealed class StoriesRequest : IApiRequest
{
    public Guid? StoryPublicId { get; set; }
    public Guid? MusicPublicId { get; set; }
}
