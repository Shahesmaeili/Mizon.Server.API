namespace Mizon.Server.Logic.Requests.Stories;

public sealed class StoryRequest : IApiRequest
{
    public Guid StoryPublicId { get; set; }
}
