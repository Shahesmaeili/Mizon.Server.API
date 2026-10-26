namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicResponse : IApiResponse
{
    public string Title { get; set; } = null!;

    public string? PersianTitle { get; set; }

    public DateTimeOffset? ReleaseDateTime { get; set; }

    public bool LikedByMe { get; set; }

    public int LikesCount { get; set; }

    public int ViewsCount { get; set; }

    public List<MusicLanguageResponse> MusicLanguages { get; set; } = [];

    public List<MusicGenreResponse> MusicGenres { get; set; } = [];

    public MusicAlbumResponse? MusicAlbum { get; set; }

    public MusicLabelResponse? MusicLabel { get; set; }

    public MusicCoverResponse? MusicCover { get; set; }
}

public sealed class MusicLanguageResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
}

public sealed class MusicAlbumResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
}

public sealed class MusicLabelResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
}

public sealed class MusicGenreResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
}

public sealed class MusicCoverResponse
{
    public Guid MediaPublicId { get; set; }
    public long FileSize { get; set; }
    public string Blurhash { get; set; } = null!;
    public short Width { get; set; }
    public short Height { get; set; }
    public string PrimaryAccentColor { get; set; } = null!;
}
