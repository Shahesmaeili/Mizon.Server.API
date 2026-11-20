namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicCommentTagSearchRequest : IApiRequest
{
    public string Type { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
}
