namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicStemMixRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
