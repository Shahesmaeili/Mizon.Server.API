namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicLyricResponse : IApiResponse
{
    public List<MusicLyricLanguageResponse> MusicLyricLanguages { get; set; } = [];
}

public sealed class MusicLyricLanguageResponse
{
    public Guid MusicLyricPublicId { get; set; }
    public Guid LanguagePublicId { get; set; }
    public string LanguageTitle { get; set; } = null!;
    public string? LanguagePersianTitle { get; set; }
}
