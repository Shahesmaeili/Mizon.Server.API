namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
