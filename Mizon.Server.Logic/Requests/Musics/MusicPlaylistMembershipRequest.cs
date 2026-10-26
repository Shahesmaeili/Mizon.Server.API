namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicPlaylistMembershipRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
    public List<Guid> MusicPlaylistPublicIds { get; set; } = [];
}
