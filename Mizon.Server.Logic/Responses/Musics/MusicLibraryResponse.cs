namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicLibraryResponse : IApiResponse
{
    public List<MusicLibraryPlaylistItemResponse> Playlists { get; set; } = [];
    public int TotalPlaylistsCount { get; set; }
    public long TotalMusicsCount { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class MusicLibraryPlaylistItemResponse
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public int MusicsCount { get; set; }
    public Guid? LastMusicCoverMediaPublicId { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

