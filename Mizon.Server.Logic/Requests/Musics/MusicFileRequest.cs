namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicFileRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
