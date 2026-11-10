using System.Security.Cryptography;

namespace Mizon.Server.Logic.Operations.Musics;

// Explicit local provisioning, never called implicitly by the public read endpoint.
// All inserts stay in the business layer; migrations only define schema/lookups.
public sealed class MusicFeaturesInitializeOperation(MizonDatabaseContext database)
{
    public async Task<int> ExecuteAsync(string? videoPath, CancellationToken cancellationToken = default)
    {
        if (await database.MusicFeatures.AnyAsync(cancellationToken)) return 0;
        var candidates = await database.MusicCovers.AsNoTracking()
            .Where(x => x.Music!.IsActive)
            .OrderBy(x => x.MusicId)
            .Select(x => new { x.MusicId, x.MediaId, x.Media!.MediaKeyId, x.Media.StoragePath })
            .Take(32)
            .ToListAsync(cancellationToken);
        var covers = candidates.Where(x => IsExistingLocalFile(x.StoragePath)).Take(3).ToList();
        if (covers.Count == 0) return 0;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var cover in covers)
            database.MusicFeatures.Add(new()
            {
                MusicId = cover.MusicId, ContentMediaId = cover.MediaId, PreviewMediaId = cover.MediaId,
                MusicFeatureContentTypeId = 1, DisplaySeconds = 8, IsActive = true
            });

        if (!string.IsNullOrWhiteSpace(videoPath))
        {
            var fullPath = Path.GetFullPath(videoPath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("The featured preview video was not found.", fullPath);
            var videoCover = covers.FirstOrDefault(x => Uri.TryCreate(x.StoragePath, UriKind.Absolute, out var uri) && uri.IsFile) ?? covers[0];
            var encryptionTypeId = await database.MediaEncryptionTypes.AsNoTracking()
                .Where(x => x.Title == "None").Select(x => x.Id).SingleAsync(cancellationToken);
            var storageTypeId = await database.MediaStorageTypes.AsNoTracking()
                .Where(x => x.Title == "Local").Select(x => x.Id).SingleAsync(cancellationToken);
            var info = new FileInfo(fullPath);
            const int chunkSize = 256 * 1024;
            await using var stream = File.OpenRead(fullPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            var media = new Media
            {
                StoragePath = new Uri(fullPath).AbsoluteUri, FileName = info.Name, FileSize = info.Length,
                FileHash = hash, ChunkSize = chunkSize, TotalChunks = checked((int)((info.Length + chunkSize - 1) / chunkSize)),
                EncryptionIV = new byte[16], MediaKeyId = videoCover.MediaKeyId,
                MediaEncryptionTypeId = encryptionTypeId, MediaStorageTypeId = storageTypeId
            };
            database.MusicFeatures.Add(new()
            {
                MusicId = videoCover.MusicId, ContentMedia = media, PreviewMediaId = videoCover.MediaId,
                MusicFeatureContentTypeId = 2, DisplaySeconds = 8, IsActive = true
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return covers.Count + (string.IsNullOrWhiteSpace(videoPath) ? 0 : 1);
    }

    public async Task SetImageAsync(Guid featurePublicId, Guid musicPublicId, CancellationToken cancellationToken = default)
    {
        var feature = await database.MusicFeatures.Where(x => x.PublicId == featurePublicId)
            .Where(x => x.MusicFeatureContentTypeId == 1).SingleAsync(cancellationToken);
        var cover = await database.MusicCovers.AsNoTracking()
            .Where(x => x.Music!.PublicId == musicPublicId).Where(x => x.Music!.IsActive)
            .Select(x => new {x.MusicId,x.MediaId,x.Media!.StoragePath}).SingleAsync(cancellationToken);
        if (!IsExistingLocalFile(cover.StoragePath))
            throw new InvalidOperationException("Local provisioning requires an existing preview image.");
        feature.MusicId = cover.MusicId;
        feature.ContentMediaId = cover.MediaId;
        feature.PreviewMediaId = cover.MediaId;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static bool IsExistingLocalFile(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile && File.Exists(uri.LocalPath);
}
