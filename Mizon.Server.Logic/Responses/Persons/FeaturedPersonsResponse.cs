namespace Mizon.Server.Logic.Responses.Persons;

public sealed class FeaturedPersonsResponse : IApiResponse
{
    public List<FeaturedPersonItem> Persons { get; set; } = [];
}

public sealed class FeaturedPersonItem
{
    public Guid PersonPublicId { get; init; }
    public short SortOrder { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public Guid? ProfileMediaPublicId { get; init; }
    public Guid? CoverMediaPublicId { get; init; }
    public Guid? ModelMediaPublicId { get; init; }
    public string? AccentColor { get; init; }
    public string? ModelAttribution { get; init; }
    public float ModelYawInDegrees { get; init; }
    public float ModelViewScale { get; init; } = 1;
    public float ModelVerticalFocus { get; init; }
    public int FollowersCount { get; init; }
    public bool IsFollowing { get; init; }
    public bool CanFollow { get; init; }
    public int MusicsCount { get; init; }
    public string? GenreTitle { get; set; }
    public string? PersianGenreTitle { get; set; }
    public int MonthlyListenersCount { get; set; }
    // UTC days, oldest first, ending on the current day.
    public int[] DailyPlays { get; set; } = [];
    public List<FeaturedPersonMusicItem> LatestMusics { get; set; } = [];
}

public sealed class FeaturedPersonMusicItem
{
    public Guid PublicId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? PersianTitle { get; init; }
    public Guid? CoverMediaPublicId { get; init; }
    public TimeSpan? Duration { get; init; }
    public int LikesCount { get; init; }
}
