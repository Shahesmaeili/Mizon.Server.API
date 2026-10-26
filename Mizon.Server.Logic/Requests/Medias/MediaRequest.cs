namespace Mizon.Server.Logic.Requests.Medias;

public sealed class MediaRequest : IApiRequest
{
    public Guid MediaPublicId { get; set; }
}
