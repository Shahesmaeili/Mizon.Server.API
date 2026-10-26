namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicReactionsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}

