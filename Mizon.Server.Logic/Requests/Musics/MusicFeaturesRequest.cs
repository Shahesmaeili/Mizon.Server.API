namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicFeaturesRequest : IApiRequest
{
    public int Limit { get; set; } = 12;
}
