namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicPlaylistDetailsRequest : IApiRequest
{
    public Guid MusicPlaylistPublicId { get; set; }
    public int PageSize { get; set; } = 30;
    public string? Cursor { get; set; }
}

