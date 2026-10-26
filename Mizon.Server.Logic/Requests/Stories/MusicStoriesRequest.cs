namespace Mizon.Server.Logic.Requests.Stories;

public sealed class MusicStoriesRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
