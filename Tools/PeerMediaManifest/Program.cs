using System.Security.Cryptography;

if (args is not [var input, var output, var requestedChunkSize] || !int.TryParse(requestedChunkSize, out var chunkSize)
    || chunkSize is < 1 or > 4 * 1024 * 1024)
{
    Console.Error.WriteLine("Usage: PeerMediaManifest <authorized-public-media-file> <manifest-directory> <database-chunk-size>");
    return 2;
}

await using var media = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read,
    bufferSize: 64 * 1024, useAsync: true);
if (media.Length <= 0 || (media.Length - 1) / chunkSize + 1 > 65_536)
    throw new InvalidDataException("The file has an unsupported piece count.");

using var fullHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
var count = (int)((media.Length - 1) / chunkSize + 1);
var digests = new byte[count * SHA256.HashSizeInBytes];
var buffer = new byte[chunkSize];
for (var index = 0; index < count; index++)
{
    var length = (int)Math.Min(media.Length - media.Position, chunkSize);
    await media.ReadExactlyAsync(buffer.AsMemory(0, length));
    fullHash.AppendData(buffer.AsSpan(0, length));
    SHA256.HashData(buffer.AsSpan(0, length), digests.AsSpan(index * SHA256.HashSizeInBytes, SHA256.HashSizeInBytes));
}
var hash = Convert.ToHexStringLower(fullHash.GetHashAndReset());
Directory.CreateDirectory(output);
var path = Path.Combine(output, $"{hash}.{chunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}.sha256");
var staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
await File.WriteAllBytesAsync(staged, digests);
try { File.Move(staged, path, overwrite: true); }
finally { File.Delete(staged); }
Console.WriteLine($"{hash} {media.Length} {count} {path}");
return 0;
