namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicContributorRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
}
