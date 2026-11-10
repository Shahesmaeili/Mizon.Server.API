using System.Security.Cryptography;
using System.Text.Json;

namespace Mizon.Server.Logic.Operations.Clients;

// Local catalog provisioning only. Public read/write endpoints never insert assets.
public sealed class ClientMoodInitializeOperation(MizonDatabaseContext database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<int> ExecuteAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath)) return 0;
        await Gate.WaitAsync(cancellationToken);
        try { return await InitializeAsync(manifestPath, cancellationToken); }
        finally { Gate.Release(); }
    }

    private async Task<int> InitializeAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        if (new FileInfo(manifestPath).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("The mood catalog is too large.");
        await using var manifest = File.OpenRead(manifestPath);
        using var document = await JsonDocument.ParseAsync(manifest, cancellationToken: cancellationToken);
        var items = new List<CatalogItem>();
        var files = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
        var emojis = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (items.Count >= 4096) throw new InvalidDataException("Too many mood entries.");
            var emoji = entry.GetProperty("emoji").GetString();
            var title = entry.GetProperty("title").GetString();
            var persianTitle = entry.TryGetProperty("persianTitle", out var persian) ? persian.GetString() : null;
            if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 64 || !emojis.Add(emoji)
                || string.IsNullOrWhiteSpace(title) || title.Length > 128 || persianTitle?.Length > 128)
                throw new InvalidDataException("Invalid or duplicate mood catalog entry.");
            var animation = await ReadAssetAsync(entry.GetProperty("animationPath").GetString()!, animation: true);
            Asset? preview = null;
            if (entry.TryGetProperty("previewPath", out var previewPath) && previewPath.ValueKind == JsonValueKind.String)
                preview = await ReadAssetAsync(previewPath.GetString()!, animation: false);
            var effects = new List<Asset>();
            if (entry.TryGetProperty("effectPaths", out var effectPaths))
                foreach (var effectPath in effectPaths.EnumerateArray())
                {
                    if (effects.Count >= 32) throw new InvalidDataException("Too many mood effects.");
                    var effect = await ReadAssetAsync(effectPath.GetString()!, animation: true);
                    if (!effects.Contains(effect)) effects.Add(effect);
                }
            items.Add(new(emoji, title.Trim(), persianTitle?.Trim(), animation, preview, effects));
        }
        if (items.Count == 0) return 0;

        var encryptionId = await database.MediaEncryptionTypes.AsNoTracking()
            .Where(x => x.Title == "None").Select(x => x.Id).SingleAsync(cancellationToken);
        var storageId = await database.MediaStorageTypes.AsNoTracking()
            .Where(x => x.Title == "Local").Select(x => x.Id).SingleAsync(cancellationToken);
        var key = await database.MediaKeys.AsNoTracking().Where(x => !x.IsRevoked)
            .OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        MediaKey? newKey = key is null
            ? new() { KeyData = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } : null;
        if (newKey is not null) database.MediaKeys.Add(newKey);
        var paths = files.Values.SelectMany(x => x.LegacyUri is null ? new[] { x.Uri } : new[] { x.Uri, x.LegacyUri }).ToArray();
        var mediaRows = await database.Medias.AsNoTracking()
            .Where(x => paths.Contains(x.StoragePath))
            .Select(x => new Media
            {
                Id = x.Id, StoragePath = x.StoragePath, FileHash = x.FileHash,
                FileName = x.FileName, FileSize = x.FileSize, ChunkSize = x.ChunkSize, TotalChunks = x.TotalChunks
            }).ToListAsync(cancellationToken);
        foreach (var media in mediaRows) database.Medias.Attach(media);
        var medias = mediaRows.GroupBy(x => x.StoragePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        // Replacing this catalog's JSON transport with its original TGS keeps
        // media identities stable. Never repurpose a media shared by other features.
        var legacyIds = files.Values
            .Where(x => !medias.ContainsKey(x.Uri))
            .Where(x => x.LegacyUri is not null)
            .Where(x => medias.ContainsKey(x.LegacyUri!))
            .Select(x => medias[x.LegacyUri!].Id).ToArray();
        var reusableIds = legacyIds.Length == 0 ? [] : await database.Medias.AsNoTracking()
            .Where(x => legacyIds.Contains(x.Id))
            .Where(x => x.AnimationClientMoodTypes.Any() || x.ClientMoodEffects.Any())
            .Where(x => !x.PreviewClientMoodTypes.Any())
            .Where(x => !x.ClientProfiles.Any())
            .Where(x => !x.MusicCovers.Any())
            .Where(x => !x.MusicFiles.Any())
            .Where(x => !x.ContentStories.Any())
            .Where(x => !x.PreviewStories.Any())
            .Where(x => !x.ContentMusicFeatures.Any())
            .Where(x => !x.PreviewMusicFeatures.Any())
            .Where(x => !database.MusicFileStems.Any(stem => stem.MediaId == x.Id))
            .Where(x => !database.Persons.Any(person => person.ProfileMediaId == x.Id))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var reusable = reusableIds.ToHashSet();
        const int chunkSize = 256 * 1024;
        foreach (var asset in files.Values)
        {
            if (!medias.TryGetValue(asset.Uri, out var media))
            {
                if (asset.LegacyUri is not null && medias.TryGetValue(asset.LegacyUri, out var legacy) && reusable.Contains(legacy.Id))
                {
                    media = legacy;
                    media.StoragePath = asset.Uri;
                }
                else
                {
                    media = new()
                    {
                        StoragePath = asset.Uri, MediaEncryptionTypeId = encryptionId, MediaStorageTypeId = storageId,
                        EncryptionIV = new byte[16], MediaKeyId = key ?? 0, MediaKey = newKey
                    };
                    database.Medias.Add(media);
                }
                medias.Add(asset.Uri, media);
            }
            media.FileName = Path.GetFileName(asset.Path);
            media.FileHash = asset.Hash;
            media.FileSize = asset.Length;
            media.ChunkSize = chunkSize;
            media.TotalChunks = checked((int)((asset.Length + chunkSize - 1) / chunkSize));
        }
        await database.SaveChangesAsync(cancellationToken);

        var types = await database.ClientMoodTypes.AsNoTracking()
            .Select(x => new ClientMoodType
            {
                Id = x.Id, Emoji = x.Emoji, Title = x.Title, PersianTitle = x.PersianTitle,
                AnimationMediaId = x.AnimationMediaId, PreviewMediaId = x.PreviewMediaId, IsActive = x.IsActive
            }).ToListAsync(cancellationToken);
        foreach (var type in types) database.ClientMoodTypes.Attach(type);
        var byEmoji = types.ToDictionary(x => x.Emoji, StringComparer.Ordinal);
        var added = 0;
        foreach (var item in items)
        {
            if (!byEmoji.TryGetValue(item.Emoji, out var type))
            {
                type = new() { Emoji = item.Emoji };
                database.ClientMoodTypes.Add(type);
                byEmoji.Add(item.Emoji, type);
                added++;
            }
            type.Title = item.Title;
            type.PersianTitle = item.PersianTitle;
            type.AnimationMediaId = medias[item.Animation.Uri].Id;
            type.PreviewMediaId = item.Preview is null ? null : medias[item.Preview.Uri].Id;
            type.IsActive = true;
        }
        await database.SaveChangesAsync(cancellationToken);

        var typeIds = items.Select(x => byEmoji[x.Emoji].Id).ToArray();
        var effectRows = await database.ClientMoodEffects.AsNoTracking()
            .Where(x => typeIds.Contains(x.ClientMoodTypeId))
            .Select(x => new ClientMoodEffect
            {
                Id = x.Id, ClientMoodTypeId = x.ClientMoodTypeId, MediaId = x.MediaId, IsActive = x.IsActive
            }).ToListAsync(cancellationToken);
        foreach (var effect in effectRows) database.ClientMoodEffects.Attach(effect);
        var effectsByType = effectRows.ToLookup(x => x.ClientMoodTypeId);
        foreach (var item in items)
        {
            var typeId = byEmoji[item.Emoji].Id;
            var mediaIds = item.Effects.Select(x => medias[x.Uri].Id).ToHashSet();
            var existing = effectsByType[typeId].ToDictionary(x => x.MediaId);
            foreach (var effect in existing.Values) effect.IsActive = mediaIds.Contains(effect.MediaId);
            foreach (var mediaId in mediaIds)
                if (!existing.ContainsKey(mediaId))
                    database.ClientMoodEffects.Add(new() { ClientMoodTypeId = typeId, MediaId = mediaId, IsActive = true });
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return added;

        async Task<Asset> ReadAssetAsync(string relative, bool animation)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Mood assets must use relative paths.");
            var fullPath = Path.GetFullPath(Path.Combine(directory, relative));
            if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A mood asset is outside the catalog directory.");
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (animation ? extension is not (".json" or ".tgs")
                : extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                throw new InvalidDataException("Unsupported mood asset format.");
            if (files.TryGetValue(fullPath, out var existing)) return existing;
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length is <= 0 or > 16 * 1024 * 1024)
                throw new InvalidDataException("A mood asset is missing or too large.");
            await using var stream = File.OpenRead(fullPath);
            if (extension == ".tgs")
            {
                var magic = new byte[2];
                await stream.ReadExactlyAsync(magic, cancellationToken);
                if (magic[0] != 0x1f || magic[1] != 0x8b)
                    throw new InvalidDataException("A TGS asset must contain gzip-compressed Lottie data.");
                stream.Position = 0;
            }
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            string? legacyUri = null;
            var relativePath = Path.GetRelativePath(directory, fullPath).Replace('\\', '/');
            if (extension == ".tgs" && (relativePath.StartsWith("originals/animations/", StringComparison.Ordinal)
                || relativePath.StartsWith("originals/effects/", StringComparison.Ordinal)))
                legacyUri = new Uri(Path.ChangeExtension(Path.Combine(directory, relativePath["originals/".Length..]), ".json")).AbsoluteUri;
            var asset = new Asset(fullPath, new Uri(fullPath).AbsoluteUri, legacyUri, info.Length, hash);
            files.Add(fullPath, asset);
            return asset;
        }
    }

    private sealed record Asset(string Path, string Uri, string? LegacyUri, long Length, string Hash);
    private sealed record CatalogItem(string Emoji, string Title, string? PersianTitle, Asset Animation, Asset? Preview, List<Asset> Effects);
}

