using System.Data;
using System.Security.Cryptography;

namespace Mizon.Server.Logic.Operations.Persons;

/// <summary>Explicit development fixtures. Existing editorial lists and media links are preserved.</summary>
public sealed class FeaturedPersonsInitializeOperation(MizonDatabaseContext database)
{
    private const int ChunkSize = 256 * 1024;
    private static readonly Guid MoeinId = new("019c0ced-73a6-74de-a2cc-c702c6933bd1");
    private static readonly Guid LeilaHatamiId = new("019c0ced-73a6-74de-a2cc-c702c6933bd2");
    private static readonly Guid ShahabHosseiniId = new("019c0ced-73a6-74de-a2cc-c702c6933bd3");

    public async Task<int> ExecuteAsync(string modelDirectory, CancellationToken token)
    {
        var directory = Path.GetFullPath(modelDirectory);
        var model = await ReadFileAsync(Path.Combine(directory, "MoeinBust.glb"), token);
        var portrait = await ReadFileAsync(Path.Combine(directory, "Moein-reference.png"), token);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var types = await database.FeaturedPersonTypes.ToDictionaryAsync(x => x.Code, token);
        var music = GetType(1, "music", "Music", "موسیقی");
        var movies = GetType(2, "movies", "Movies", "فیلم");
        var series = GetType(3, "series", "Series", "سریال");
        var populated = await database.FeaturedPersons.AsNoTracking().Select(x => x.FeaturedPersonTypeId).Distinct().ToListAsync(token);
        var added = 0;
        if (!populated.Contains(music.Id))
        {
            var moein = await GetPersonAsync(MoeinId, "نصرالله", "معین نجف‌آبادی");
            if (moein.ModelMediaId is null) moein.ModelMedia = await CreateLocalMediaAsync(model, token);
            if (moein.ProfileMediaId is null) moein.ProfileMedia = await CreateLocalMediaAsync(portrait, token);
            database.FeaturedPersons.Add(new()
            {
                FeaturedPersonType = music, Person = moein, SortOrder = 1,
                AccentColor = "#12A6ED", ModelYawInDegrees = 180,
                ModelAttribution = "Moein · AI-assisted reconstruction · Mehrshad Adkins 01 / Mehrshad.Adkins · CC BY-SA 4.0"
            });
            added++;
            var credited = await database.Persons.AsNoTracking().Where(p => p.PublicId != moein.PublicId)
                .Where(p => p.MusicContributors.Any(c => c.Music!.IsActive))
                .OrderByDescending(p => p.MusicContributors.Where(c => c.Music!.IsActive).Select(c => c.MusicId).Distinct().Count())
                .ThenBy(p => p.Id).Select(p => p.Id).Take(4).ToListAsync(token);
            foreach (var id in credited)
                database.FeaturedPersons.Add(new() { FeaturedPersonType = music, PersonId = id, SortOrder = (short)(++added) });
        }

        if (!populated.Contains(movies.Id))
        {
            var leila = await GetPersonAsync(LeilaHatamiId, "لیلا", "حاتمی");
            database.FeaturedPersons.Add(new() { FeaturedPersonType = movies, Person = leila, SortOrder = 1, AccentColor = "#E3A872" });
            added++;
        }
        if (!populated.Contains(series.Id))
        {
            var shahab = await GetPersonAsync(ShahabHosseiniId, "شهاب", "حسینی");
            database.FeaturedPersons.Add(new() { FeaturedPersonType = series, Person = shahab, SortOrder = 1, AccentColor = "#8BA4F8" });
            added++;
        }
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return added;

        FeaturedPersonType GetType(byte id, string code, string title, string persianTitle)
        {
            if (types.TryGetValue(code, out var type)) return type;
            type = new() { Id = id, Code = code, Title = title, PersianTitle = persianTitle };
            database.FeaturedPersonTypes.Add(type);
            return type;
        }

        async Task<Person> GetPersonAsync(Guid id, string firstName, string lastName) =>
            await database.Persons.FirstOrDefaultAsync(x => x.PublicId == id || (x.FirstName == firstName && x.LastName == lastName), token)
                ?? new Person { PublicId = id, FirstName = firstName, LastName = lastName };
    }

    private async Task<Media> CreateLocalMediaAsync(LocalFile file, CancellationToken token)
    {
        var encryption = await database.MediaEncryptionTypes.SingleAsync(x => x.Title == "None", token);
        var storage = await database.MediaStorageTypes.SingleAsync(x => x.Title == "Local", token);
        var keyId = await database.MediaKeys.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(token);
        return new()
        {
            StoragePath = file.Uri, FileName = file.Name, FileHash = file.Hash, FileSize = file.Size,
            MediaKeyId = keyId, MediaEncryptionTypeId = encryption.Id, MediaStorageTypeId = storage.Id,
            EncryptionIV = new byte[16], ChunkSize = ChunkSize,
            TotalChunks = checked((int)((file.Size + ChunkSize - 1) / ChunkSize))
        };
    }

    private static async Task<LocalFile> ReadFileAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > (32 * 1024 * 1024))
            throw new InvalidDataException("An artist fixture exceeds the supported media size.");
        return new(new Uri(path).AbsoluteUri, Path.GetFileName(path),
            Convert.ToHexString(await SHA256.HashDataAsync(file, token)), file.Length);
    }

    private sealed record LocalFile(string Uri, string Name, string Hash, long Size);
}
