namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicCommentCreateRequest : IApiRequest
{
    public Guid MusicPublicId { get; set; }
    public string Text { get; set; } = null!;
    public TimeSpan? Timestamp { get; set; }
}
