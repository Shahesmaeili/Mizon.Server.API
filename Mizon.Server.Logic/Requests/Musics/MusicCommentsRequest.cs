namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicCommentsRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
    public Guid? MusicCommentPublicId { get; set; }
    public Guid? ParentMusicCommentPublicId { get; set; }
    public Guid? After { get; set; }
    public int? PageSize { get; set; }
    public string? Sort { get; set; }
    public bool? TimedOnly { get; set; }
}
