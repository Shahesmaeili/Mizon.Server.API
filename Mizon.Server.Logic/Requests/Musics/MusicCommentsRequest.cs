namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicCommentsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
