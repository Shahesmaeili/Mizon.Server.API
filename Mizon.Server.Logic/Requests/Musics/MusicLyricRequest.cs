namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicLyricRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
