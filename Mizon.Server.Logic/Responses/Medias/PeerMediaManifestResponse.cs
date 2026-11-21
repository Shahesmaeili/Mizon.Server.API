namespace Mizon.Server.Logic.Responses.Medias;

public sealed class PeerMediaManifestResponse : IApiResponse
{
    public string FileHash { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public int ChunkSize { get; init; }
    public byte[] PieceSha256 { get; init; } = [];
}
