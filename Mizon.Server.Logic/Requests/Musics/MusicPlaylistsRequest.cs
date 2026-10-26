namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicPlaylistsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
