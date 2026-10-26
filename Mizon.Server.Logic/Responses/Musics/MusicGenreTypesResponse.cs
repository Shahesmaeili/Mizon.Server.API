namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicGenreTypesResponse : IApiResponse
{
    public List<MusicGenreTypeItemResponse> MusicGenreTypes { get; set; } = [];
}

public sealed class MusicGenreTypeItemResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
    public string AccentColorHex { get; set; } = null!;
    public string IconSymbol { get; set; } = null!;
    public int MusicsCount { get; set; }
}
