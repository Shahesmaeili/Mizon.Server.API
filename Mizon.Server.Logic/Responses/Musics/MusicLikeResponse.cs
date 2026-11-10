namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicLikeResponse : IApiResponse
{
    public Guid MusicPublicId { get; set; }
    public bool LikedByMe { get; set; }
    public int LikesCount { get; set; }
}
