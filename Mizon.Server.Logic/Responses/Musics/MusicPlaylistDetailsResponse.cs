namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicPlaylistDetailsResponse : IApiResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public bool IsOwner { get; set; }
    public int MusicsCount { get; set; }
    public List<MusicTabMusicItemResponse> Musics { get; set; } = [];
    public string? NextCursor { get; set; }
}

