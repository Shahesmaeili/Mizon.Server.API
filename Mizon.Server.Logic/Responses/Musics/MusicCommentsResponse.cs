namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicCommentsResponse : IApiResponse
{
    public List<MusicCommentItemResponse> MusicComments { get; set; } = [];
    public Guid? Next { get; set; }
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
    public Guid? ParentMusicCommentPublicId { get; set; }
    public DateTimeOffset CreateDateTime { get; set; }
    public string? Platform { get; set; }
    public int Replies { get; set; }
    public int Likes { get; set; }
    public int Dislikes { get; set; }
    public int MyVote { get; set; }
    public List<MusicCommentTagItem> Tags { get; set; } = [];
}

public sealed record MusicCommentTagItem(string Type, Guid PublicId, string Label, Guid? NavigationPublicId = null);
