namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicPlaylistsResponse : IApiResponse
{
    public List<MusicPlaylistListItemResponse> MusicPlaylists { get; set; } = [];
}

public sealed class MusicPlaylistListItemResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public int MusicsCount { get; set; }
    public Guid? LastMusicCoverMediaPublicId { get; set; }
    public bool ContainsCurrentMusic { get; set; }
}
