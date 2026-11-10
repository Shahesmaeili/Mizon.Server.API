namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicLikeRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
    public bool Liked { get; set; }
}
