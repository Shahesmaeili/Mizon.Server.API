namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicFileResponse : IApiResponse
{
    public Guid MusicFilePublicId { get; set; }
    public Guid MediaPublicId { get; set; }
    public int TotalChunks { get; set; }
    public int ChunkSize { get; set; }
    public string FileHash { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = null!;
    public TimeSpan Duration { get; set; }
}
