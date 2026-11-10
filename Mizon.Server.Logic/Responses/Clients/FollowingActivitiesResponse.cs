namespace Mizon.Server.Logic.Responses.Clients;

public sealed class FollowingActivitiesResponse : IApiResponse
{
    public bool HasFollowings { get; set; }
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
    public List<FollowingActivityItemResponse> Activities { get; set; } = [];
}

public sealed class FollowingActivityItemResponse
{
    public string ActivityId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset CreateDateTime { get; set; }
    public Guid ClientPublicId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NickName { get; set; }
    public string? Username { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }
    public Guid? MusicPublicId { get; set; }
    public Guid? MusicCommentPublicId { get; set; }
    public Guid? MusicPlaylistPublicId { get; set; }
    public Guid? StoryPublicId { get; set; }
    public bool StoryViewedByMe { get; set; }
    public string? StoryContentTypeTitle { get; set; }
    public int? TaggedMusicsCount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PersianTitle { get; set; }
    public Guid? CoverMediaPublicId { get; set; }
    public string? CommentText { get; set; }
    public string? ReactionEmoji { get; set; }
    public int? PlaylistMusicsCount { get; set; }
}
