namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicTabMusicsResponse : IApiResponse
{
    public List<MusicTabMusicItemResponse> Musics { get; set; } = [];
}

public sealed class MusicTabMusicItemResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
    public int LikesCount { get; set; }
    public Guid? CoverMediaPublicId { get; set; }
    public List<MusicTabContributorItemResponse> Contributors { get; set; } = [];
}

public sealed class MusicTabContributorItemResponse
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
}
