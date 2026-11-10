namespace Mizon.Server.Logic.Requests.Musics;

public sealed class MusicLibraryRequest : IApiRequest
{
    public int PageSize { get; set; } = 5;
    public string? Cursor { get; set; }
}

