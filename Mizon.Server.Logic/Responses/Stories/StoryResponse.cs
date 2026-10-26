namespace Mizon.Server.Logic.Responses.Stories;

public sealed class StoriesResponse : IApiResponse
{
    public List<StoryListItemResponse> Stories { get; set; } = [];
}

public sealed class StoryListItemResponse
{
    public Guid PublicId { get; set; }
    public string CreatorFirstName { get; set; } = null!;
    public string CreatorLastName { get; set; } = null!;
    public Guid? CreatorProfileMediaPublicId { get; set; }
    public Guid PreviewMediaPublicId { get; set; }
    public string? Title { get; set; }
    public bool ViewedByMe { get; set; }
}

public sealed class StoryResponse : IApiResponse
{
    public Guid PublicId { get; set; }
    public Guid CreatorClientPublicId { get; set; }
    public string CreatorFirstName { get; set; } = null!;
    public string CreatorLastName { get; set; } = null!;
    public string? CreatorUsername { get; set; }
    public Guid? CreatorProfileMediaPublicId { get; set; }
    public Guid StoryContentTypePublicId { get; set; }
    public string StoryContentTypeTitle { get; set; } = null!;
    public string? StoryContentTypePersianTitle { get; set; }
    public Guid ContentMediaPublicId { get; set; }
    public Guid? PreviewMediaPublicId { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset? ExpireDateTime { get; set; }
    public bool ViewedByMe { get; set; }
    public bool LikedByMe { get; set; }
    public int ViewsCount { get; set; }
    public int LikesCount { get; set; }
    public List<StoryWidgetResponse> Widgets { get; set; } = [];
}

public sealed class StoryWidgetResponse
{
    public Guid PublicId { get; set; }
    public Guid StoryWidgetTypePublicId { get; set; }
    public Guid StoryWidgetStylePublicId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Rotation { get; set; }
    public short ZIndex { get; set; }
    public StoryMusicWidgetResponse? Music { get; set; }
    public StoryClientWidgetResponse? Client { get; set; }
}

public sealed class StoryMusicWidgetResponse
{
    public Guid MusicPublicId { get; set; }
    public string Title { get; set; } = null!;
    public Guid? CoverMediaPublicId { get; set; }
}

public sealed class StoryClientWidgetResponse
{
    public Guid ClientPublicId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Username { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }
}
