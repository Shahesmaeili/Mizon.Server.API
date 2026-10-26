namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicContributorMusicsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
