using System.Security.Cryptography;

namespace Mizon.Server.Logic.Operations.Musics;

/// <summary>
/// Explicit operator-only refresh of the known development story/featured fixtures.
/// Never registered as a public endpoint or invoked by a read operation/startup.
/// Original Media rows, profiles, music covers and user activity remain intact.
/// </summary>
public sealed class MusicHomeArtworkInitializeOperation(MizonDatabaseContext database)
{
    private const string FilePrefix = "music-home-artwork-v1-";
    private const int ChunkSize = 256 * 1024;

    private static readonly Fixture[] Fixtures =
    [
        new(false, new("01a04502-0f85-75af-9c06-a84022f547aa"), "electric-guitar", false, 9, 10),
        new(false, new("01a04502-1015-7667-8c95-9360228267ff"), "blue-stage", true, 12, 13),
        new(false, new("01a04502-101e-7006-81cf-b77aeeed7686"), "headphones", false, 15, 16),
        new(false, new("01a04502-1028-7dc7-bca2-31ab0f9f7d71"), "festival-crowd", true, 18, 19),
        new(false, new("01a04502-1031-788b-92c7-8fa380ea998c"), "blue-live-guitar", false, 21, 22),
        new(false, new("01a04502-103b-764a-a2bb-618e1a851cbb"), "electric-guitar", true, 24, 25),
        new(false, new("01a04502-1045-7a87-b2c3-07e2f45b03e7"), "blue-stage", false, 27, 28),
        new(false, new("01a04502-104f-7c2e-801e-1afb7ffcd531"), "headphones", true, 30, 31),
        new(false, new("01a04502-1059-7091-b2a8-82df09225636"), "festival-crowd", false, 33, 34),
        new(false, new("01a04502-1064-7c87-b405-fd88cea43b1a"), "studio-microphone", true, 36, 37),
        new(false, new("01a1b294-b4ee-7061-a0bc-2347ae56025c"), "studio-microphone", false, 9, 9),
        new(false, new("01a1b294-b4ee-77bd-9683-6de24c9cf576"), "blue-live-guitar", true, 12, 13),
        new(true, new("01a1b4f5-26a3-7f39-a8b0-de63d59757e0"), "blue-stage", false, 11, 11),
        new(true, new("01a1b4f5-26a3-77c7-a5dd-e0deb0d5b09e"), "festival-crowd", false, 8, 8),
        new(true, new("01a1b4f5-26a3-77a9-9826-ed369a1adfd3"), "studio-microphone", false, 10, 10),
        new(true, new("01a1b4f5-26a3-7835-a9d1-93575755fb74"), "blue-live-guitar", true, 45, 8)
    ];

    public async Task<MusicHomeArtworkPlan> GetPlanAsync(string artworkDirectory, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(artworkDirectory);
        var files = new Dictionary<string, MusicHomeArtworkFile>(StringComparer.Ordinal);
        foreach (var name in Fixtures.SelectMany(x => new[] { x.ContentFile, x.PreviewFile }).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                throw new InvalidOperationException($"Music home artwork is missing or empty: {name}.");
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            files.Add(name, new(name, new Uri(path).AbsoluteUri, hash, info.Length));
        }

        var storyIds = Fixtures.Where(x => !x.IsFeature).Select(x => x.PublicId).ToArray();
        var stories = await database.Stories.AsNoTracking().Where(x => storyIds.Contains(x.PublicId))
            .Select(x => new CurrentItem(false, x.PublicId, x.StoryContentType!.Title, x.ContentMediaId, x.PreviewMediaId,
                x.ContentMedia!.FileName, x.PreviewMedia == null ? null : x.PreviewMedia.FileName))
            .ToListAsync(cancellationToken);
        var featureIds = Fixtures.Where(x => x.IsFeature).Select(x => x.PublicId).ToArray();
        var features = await database.MusicFeatures.AsNoTracking().Where(x => featureIds.Contains(x.PublicId))
            .Select(x => new CurrentItem(true, x.PublicId, x.MusicFeatureContentType!.Code, x.ContentMediaId, x.PreviewMediaId,
                x.ContentMedia!.FileName, x.PreviewMedia == null ? null : x.PreviewMedia.FileName))
            .ToListAsync(cancellationToken);
        var current = stories.Concat(features).ToDictionary(x => x.PublicId);
        var items = new List<MusicHomeArtworkItem>(Fixtures.Length);
        foreach (var fixture in Fixtures)
        {
            if (!current.TryGetValue(fixture.PublicId, out var item))
                throw new InvalidOperationException($"The expected development fixture is missing: {fixture.PublicId}.");
            var type = fixture.IsVideo ? "video" : "image";
            if (!string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The fixture content type has changed: {fixture.PublicId}.");
            var original = item.ContentId == fixture.OriginalContentId && item.PreviewId == fixture.OriginalPreviewId;
            var previouslyProvisioned = item.ContentFile == FilePrefix + fixture.ContentFile && item.PreviewFile == FilePrefix + fixture.PreviewFile;
            if (!original && !previouslyProvisioned)
                throw new InvalidOperationException($"The fixture has independently changed artwork; it will not be overwritten: {fixture.PublicId}.");
            items.Add(new(fixture.IsFeature, fixture.PublicId, item.ContentId, item.PreviewId, files[fixture.ContentFile], files[fixture.PreviewFile]));
        }
        return new(items, files.Values.ToArray());
    }

    public async Task<MusicHomeArtworkResult> ExecuteAsync(string artworkDirectory, CancellationToken cancellationToken = default)
    {
        // Validate and hash all files before opening the transaction. Missing files cannot leave partial records.
        var plan = await GetPlanAsync(artworkDirectory, cancellationToken);
        var encryptionTypeId = await database.MediaEncryptionTypes.AsNoTracking()
            .Where(x => x.Title == "None").Select(x => x.Id).SingleAsync(cancellationToken);
        var storageTypeId = await database.MediaStorageTypes.AsNoTracking()
            .Where(x => x.Title == "Local").Select(x => x.Id).SingleAsync(cancellationToken);
        var referenceMediaId = plan.Items[0].PreviousContentMediaId;
        var keyId = await database.Medias.AsNoTracking().Where(x => x.Id == referenceMediaId)
            .Select(x => x.MediaKeyId).SingleAsync(cancellationToken);
        var paths = plan.Files.Select(x => x.StoragePath).ToArray();
        var existing = await database.Medias.AsNoTracking().Where(x => paths.Contains(x.StoragePath))
            .Where(x => x.MediaEncryptionTypeId == encryptionTypeId).Where(x => x.MediaStorageTypeId == storageTypeId)
            .Select(x => new ExistingMedia(x.Id, x.FileName, x.StoragePath, x.FileHash, x.FileSize))
            .ToListAsync(cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var mediaIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var inserted = new Dictionary<string, Media>(StringComparer.Ordinal);
        foreach (var file in plan.Files)
        {
            var reusable = existing.FirstOrDefault(x => x.FileName == FilePrefix + file.Name &&
                x.StoragePath == file.StoragePath && x.Hash == file.Hash && x.Size == file.Size);
            if (reusable is not null)
            {
                mediaIds.Add(file.Name, reusable.Id);
                continue;
            }
            var media = new Media
            {
                StoragePath = file.StoragePath, FileName = FilePrefix + file.Name, FileSize = file.Size, FileHash = file.Hash,
                MediaKeyId = keyId, MediaEncryptionTypeId = encryptionTypeId, MediaStorageTypeId = storageTypeId,
                EncryptionIV = new byte[16], ChunkSize = ChunkSize,
                TotalChunks = checked((int)((file.Size + ChunkSize - 1) / ChunkSize))
            };
            inserted.Add(file.Name, media);
            database.Medias.Add(media);
        }
        if (inserted.Count > 0)
            await database.SaveChangesAsync(cancellationToken);
        foreach (var (name, media) in inserted) mediaIds.Add(name, media.Id);

        var storiesChanged = 0;
        var featuresChanged = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var item in plan.Items)
        {
            var contentId = mediaIds[item.Content.Name];
            var previewId = mediaIds[item.Preview.Name];
            if (item.PreviousContentMediaId == contentId && item.PreviousPreviewMediaId == previewId) continue;
            int count;
            if (item.IsFeature)
            {
                count = await database.MusicFeatures.Where(x => x.PublicId == item.PublicId)
                    .Where(x => x.ContentMediaId == item.PreviousContentMediaId).Where(x => x.PreviewMediaId == item.PreviousPreviewMediaId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ContentMediaId, contentId)
                        .SetProperty(x => x.PreviewMediaId, previewId).SetProperty(x => x.UpdateDateTime, now), cancellationToken);
                featuresChanged += count;
            }
            else
            {
                count = await database.Stories.Where(x => x.PublicId == item.PublicId)
                    .Where(x => x.ContentMediaId == item.PreviousContentMediaId).Where(x => x.PreviewMediaId == item.PreviousPreviewMediaId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ContentMediaId, contentId)
                        .SetProperty(x => x.PreviewMediaId, previewId).SetProperty(x => x.UpdateDateTime, now), cancellationToken);
                storiesChanged += count;
            }
            if (count != 1) throw new DbUpdateConcurrencyException("The fixture changed during artwork replacement; the transaction was rolled back.");
        }
        await transaction.CommitAsync(cancellationToken);
        return new(storiesChanged, featuresChanged, inserted.Count);
    }

    public async Task VerifyAsync(string artworkDirectory, CancellationToken cancellationToken = default)
    {
        var plan = await GetPlanAsync(artworkDirectory, cancellationToken);
        var ids = plan.Items.SelectMany(x => new[] { x.PreviousContentMediaId, x.PreviousPreviewMediaId ?? 0 }).Distinct().ToArray();
        var records = await database.Medias.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new ExistingMedia(x.Id, x.FileName, x.StoragePath, x.FileHash, x.FileSize))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var item in plan.Items)
        {
            Verify(records[item.PreviousContentMediaId], item.Content);
            if (item.PreviousPreviewMediaId is not int previewId)
                throw new InvalidOperationException("A music home fixture has no preview after replacement.");
            Verify(records[previewId], item.Preview);
        }
    }

    private static void Verify(ExistingMedia media, MusicHomeArtworkFile file)
    {
        if (media.FileName != FilePrefix + file.Name || media.StoragePath != file.StoragePath || media.Hash != file.Hash || media.Size != file.Size)
            throw new InvalidOperationException($"Music home artwork verification failed for {file.Name}.");
    }

    private sealed record Fixture(bool IsFeature, Guid PublicId, string Asset, bool IsVideo, int OriginalContentId, int OriginalPreviewId)
    {
        public string ContentFile => Asset + (IsVideo ? ".mp4" : ".jpg");
        public string PreviewFile => Asset + (IsVideo ? "-poster.jpg" : ".jpg");
    }
    private sealed record CurrentItem(bool IsFeature, Guid PublicId, string Type, int ContentId, int? PreviewId, string ContentFile, string? PreviewFile);
    private sealed record ExistingMedia(int Id, string FileName, string StoragePath, string Hash, long Size);
}

public sealed record MusicHomeArtworkFile(string Name, string StoragePath, string Hash, long Size);
public sealed record MusicHomeArtworkItem(bool IsFeature, Guid PublicId, int PreviousContentMediaId, int? PreviousPreviewMediaId,
    MusicHomeArtworkFile Content, MusicHomeArtworkFile Preview);
public sealed record MusicHomeArtworkPlan(IReadOnlyList<MusicHomeArtworkItem> Items, IReadOnlyList<MusicHomeArtworkFile> Files);
public sealed record MusicHomeArtworkResult(int StoriesChanged, int FeaturesChanged, int MediaInserted);
