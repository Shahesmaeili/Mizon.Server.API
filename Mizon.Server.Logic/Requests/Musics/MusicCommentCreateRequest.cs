namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicCommentCreateRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
    public string Text { get; set; } = null!;
    public TimeSpan? Timestamp { get; set; }
    public Guid? ParentMusicCommentPublicId { get; set; }
    public List<MusicCommentTagRequest>? Tags { get; set; }
}

public sealed record MusicCommentTagRequest(string Type, Guid PublicId);
