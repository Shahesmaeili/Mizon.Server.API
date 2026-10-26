namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicCommentsResponse : IApiResponse
{
    public List<MusicCommentItemResponse> MusicComments { get; set; } = [];
}

public sealed class MusicCommentItemResponse
{
    public Guid PublicId { get; set; }
    public string Text { get; set; } = null!;
    public TimeSpan? Timestamp { get; set; }
    public Guid ClientPublicId { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Username { get; set; }
}
