namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicAlbumMusicsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
