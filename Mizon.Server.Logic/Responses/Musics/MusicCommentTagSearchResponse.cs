namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicCommentTagSearchResponse : IApiResponse
{
    public List<MusicCommentTagItem> Tags { get; set; } = [];
}
