namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicFeaturesResponse : IApiResponse
{
    public List<MusicFeatureItemResponse> Items { get; set; } = [];
}

public sealed class MusicFeatureItemResponse
{
    public Guid PublicId { get; set; }
    public Guid MusicPublicId { get; set; }
    public Guid MediaPublicId { get; set; }
    public Guid? PreviewMediaPublicId { get; set; }
    public string ContentTypeCode { get; set; } = null!;
    public int DisplaySeconds { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
    public double? DurationSeconds { get; set; }
    public Guid? CoverMediaPublicId { get; set; }
    public string? PrimaryAccentColor { get; set; }
    public int LikesCount { get; set; }
    public int ViewsCount { get; set; }
    public bool LikedByMe { get; set; }
    public List<MusicFeatureContributorResponse> Contributors { get; set; } = [];
    public List<MusicFeatureGenreResponse> Genres { get; set; } = [];
}

public sealed class MusicFeatureContributorResponse
{
    public Guid PersonPublicId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public Guid? ProfileMediaPublicId { get; set; }
}

public sealed class MusicFeatureGenreResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
}
