namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicViewRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
