namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicLyricLineRequest : IApiRequest
{
    public Guid MusicLyricPublicId { get; set; }
}
