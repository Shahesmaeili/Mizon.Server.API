using System.Security.Cryptography;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.Logic.Operations.Medias;

/// <summary>Reads precomputed digests for media the deployment explicitly allows clients to share.</summary>
public sealed class PublicPeerMediaManifestStore
{
    private readonly string? _directory;

    public PublicPeerMediaManifestStore(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory))
            _directory = Path.GetFullPath(directory);
    }

    public bool IsEnabled => _directory is not null;

    public async ValueTask<PeerMediaManifestResponse?> ReadAsync(MediaResponse media, CancellationToken token)
    {
        if (_directory is null || media.FileHash is not { Length: 64 } || !media.FileHash.All(Uri.IsHexDigit)
            || media.FileSize <= 0 || media.ChunkSize is < 1 or > 4 * 1024 * 1024) return null;
        var partCount = (media.FileSize - 1) / media.ChunkSize + 1;
        if (partCount != media.TotalChunks || partCount is < 1 or > 65_536) return null;

        var hash = media.FileHash.ToLowerInvariant();
        var chunkSize = media.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(_directory, $"{hash}.{chunkSize}.sha256");
        var length = checked(media.TotalChunks * SHA256.HashSizeInBytes);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != length) return null;
            var pieces = new byte[length];
            await stream.ReadExactlyAsync(pieces, token);
            return new() { FileHash = hash, FileSize = media.FileSize, ChunkSize = media.ChunkSize, PieceSha256 = pieces };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
