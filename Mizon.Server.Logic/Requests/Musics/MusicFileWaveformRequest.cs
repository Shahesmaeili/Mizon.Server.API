namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicFileWaveformRequest : IApiRequest
{
    public Guid MusicFilePublicId { get; set; }
}
