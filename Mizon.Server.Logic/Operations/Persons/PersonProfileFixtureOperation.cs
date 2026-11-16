using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Mizon.Server.Logic.Operations.Persons;

/// <summary>Local, opt-in artist fixtures with independent media and relationship data.</summary>
public sealed class PersonProfileFixtureOperation(MizonDatabaseContext database)
{
    private const int SampleRate = 22050;
    private const int ChunkSize = 256 * 1024;
    private static readonly Artist[] Catalog =
    [
        new("آوا", "سپهر", "#4D75FF", "Blue Signal", "سیگنال آبی", "Distant Lights", "نورهای دور"),
        new("بردیا", "آذر", "#F38E56", "Copper Sky", "آسمان مسی", "Desert Radio", "رادیوی کویر"),
        new("نیلا", "سبز", "#45CEA6", "Northern Wind", "باد شمال", "Green Silence", "سکوت سبز"),
        new("رامین", "شباهنگ", "#EB5370", "Red Pulse", "نبض سرخ", "After the Show", "بعد از اجرا"),
        new("دریا", "نیوند", "#B880F6", "Violet Keys", "کلاویه‌های بنفش", "Glass Hall", "تالار شیشه‌ای"),
        new("سارا", "کهربا", "#FFC66F", "Golden Room", "اتاق طلایی", "Low Thunder", "رعد آرام"),
        new("نوید", "آوین", "#51D5DC", "Tape Memory", "خاطرهٔ نوار", "Deep Channel", "کانال عمیق"),
        new("یلدا", "فیروزه", "#7F91F6", "Rooftop Dawn", "سپیدهٔ پشت‌بام", "Last Train", "آخرین قطار"),
        new("کیان", "مهرگان", "#EF86A6", "Velvet Night", "شب مخملی", "Quiet Curtains", "پرده‌های خاموش"),
        new("مهسا", "ارغوان", "#EF6BC9", "Magenta Hour", "ساعت ارغوانی", "Soft Orbit", "مدار نرم")
    ];

    public async Task<int> ExecuteAsync(string artworkDirectory, CancellationToken token)
    {
        var root = Path.GetFullPath(artworkDirectory);
        var ids = Catalog.Select((_, index) => PersonId(index)).ToArray();
        if (await database.Persons.CountAsync(x => ids.Contains(x.PublicId), token) == Catalog.Length) return 0;

        var prepared = new List<ArtistFiles>(Catalog.Length);
        for (var index = 0; index < Catalog.Length; index++)
        {
            var directory = Path.Combine(root, $"{index + 1:00}");
            var portrait = await ReadFileAsync(Path.Combine(directory, "portrait.jpg"), token);
            var cover = await ReadFileAsync(Path.Combine(directory, "cover.jpg"), token);
            var model = await ReadFileAsync(Path.Combine(directory, "performer.glb"), token);
            var first = await CreateTrackAsync(root, index, 0, token);
            var second = await CreateTrackAsync(root, index, 1, token);
            prepared.Add(new(portrait, cover, model, first, second));
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var existing = await database.Persons.CountAsync(x => ids.Contains(x.PublicId), token);
        if (existing == Catalog.Length) return 0;
        if (existing > 0) throw new InvalidOperationException("The local profile fixture catalog is incomplete.");

        var type = await database.FeaturedPersonTypes.SingleAsync(x => x.Code == "music", token);
        var position = await database.FeaturedPersons.Where(x => x.FeaturedPersonTypeId == type.Id)
            .Select(x => (short?)x.SortOrder).MaxAsync(token) ?? 0;
        var key = await database.MediaKeys.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(token);
        var encryption = await database.MediaEncryptionTypes.SingleAsync(x => x.Title == "None", token);
        var storage = await database.MediaStorageTypes.SingleAsync(x => x.Title == "Local", token);
        var jpg = await database.ImageFormats.SingleAsync(x => x.Title == "jpg", token);
        var composer = await database.MusicContributorTypes.SingleAsync(x => x.Title == "Composer", token);
        var privacy = await database.ClientPrivacyTypes.SingleAsync(x => x.Title == "Public", token);
        var following = await database.ClientFollowStatusTypes.SingleAsync(x => x.Title == "Following", token);
        var follow = await database.ClientFollowActionTypes.SingleAsync(x => x.Title == "Follow", token);
        var platform = await database.PlatformVersions.OrderBy(x => x.Id).FirstAsync(token);
        var genres = await database.MusicGenreTypes.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).Take(6).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;

        var artists = Catalog.Select((artist, index) => new Person
        {
            PublicId = ids[index], FirstName = artist.FirstName, LastName = artist.LastName,
            ProfileMedia = MediaFor(prepared[index].Portrait), CoverMedia = MediaFor(prepared[index].Cover),
            ModelMedia = MediaFor(prepared[index].Model),
            LinkedClient = new() { FirstName = artist.FirstName, LastName = artist.LastName, ClientPrivacyType = privacy }
        }).ToArray();
        database.Persons.AddRange(artists);
        for (var index = 0; index < artists.Length; index++)
        {
            var artist = artists[index];
            var other = artists[(index + 1) % artists.Length];
            var another = artists[(index + 3) % artists.Length];
            var data = Catalog[index];
            var files = prepared[index];
            database.FeaturedPersons.Add(new()
            {
                FeaturedPersonType = type, Person = artist, SortOrder = checked((short)(++position)),
                AccentColor = data.Accent, ModelViewScale = 1.08f,
                ModelAttribution = "Mizon development fixture · original synthetic artist sculpture"
            });
            AddFollow(artist.LinkedClient!, other.LinkedClient, null);
            AddFollow(artist.LinkedClient!, null, another);
            if (index % 2 == 0) AddFollow(artist.LinkedClient!, null, other);
            AddTrack(artist, other.LinkedClient!, index, 0, files.First, data.First, data.FirstPersian);
            AddTrack(artist, another.LinkedClient!, index, 1, files.Second, data.Second, data.SecondPersian);
        }
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return artists.Length;

        Media MediaFor(LocalFile file) => new()
        {
            StoragePath = file.Uri, FileName = file.Name, FileHash = file.Hash, FileSize = file.Size,
            MediaKeyId = key, MediaEncryptionTypeId = encryption.Id, MediaStorageTypeId = storage.Id,
            EncryptionIV = new byte[16], ChunkSize = ChunkSize,
            TotalChunks = checked((int)((file.Size + ChunkSize - 1) / ChunkSize))
        };

        void AddFollow(Client sender, Client? client, Person? person) => database.ClientFollows.Add(new()
        {
            FollowerClient = sender, FollowedClient = client, FollowedPerson = person,
            ClientFollowStatusType = following, FollowDateTime = now,
            ClientFollowHistories =
            [
                new() { ActorClient = sender, ClientFollowStatusType = following, ClientFollowActionType = follow }
            ]
        });

        void AddTrack(Person artist, Client listener, int index, int variant, TrackFile track, string title, string persianTitle)
        {
            var music = new Music
            {
                Title = title, PersianTitle = persianTitle, IsActive = true, ReleaseDateTime = now.AddDays(-index * 2 - variant),
                MusicCover = new()
                {
                    Media = artist.ProfileMedia, ImageFormat = jpg, Width = 384, Height = 384,
                    PrimaryAccentColor = Catalog[index].Accent, Blurhash = EncodeSolidBlurhash(Catalog[index].Accent)
                },
                MusicFiles = [new() { Media = MediaFor(track.File), Duration = track.Duration, MusicFileWaveforms = track.Waveforms }],
                MusicContributors = [new() { MusicContributorType = composer, Person = artist }],
                MusicLikes = [new() { Client = listener, PlatformVersion = platform }],
                MusicPlays = [new() { Client = listener, PlatformVersion = platform, PlaybackPublicId = Guid.CreateVersion7() }]
            };
            if (genres.Count != 0) music.MusicGenres.Add(new() { MusicGenreTypeId = genres[(index + variant) % genres.Count] });
            database.Musics.Add(music);
        }
    }

    public static Guid PersonId(int index) => new($"7a602a65-46bc-4a81-93c9-e87ef5c7{index + 1:D4}");

    private static string EncodeSolidBlurhash(string accent)
    {
        const string base83 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";
        var value = Convert.ToInt32(accent[1..], 16);
        return $"00{base83[value / (83 * 83 * 83)]}{base83[value / (83 * 83) % 83]}{base83[value / 83 % 83]}{base83[value % 83]}";
    }

    private static async Task<LocalFile> ReadFileAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 32 * 1024 * 1024)
            throw new InvalidDataException("An artist fixture file is empty or too large.");
        return new(new Uri(path).AbsoluteUri, Path.GetFileName(path),
            Convert.ToHexString(await SHA256.HashDataAsync(stream, token)), stream.Length);
    }

    private static async Task<TrackFile> CreateTrackAsync(string root, int index, int variant, CancellationToken token)
    {
        var duration = 10 + index % 4 + variant * 2;
        const int blockSize = SampleRate / 10;
        var count = duration * SampleRate;
        var directory = Path.Combine(root, "generated-audio");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"artist-{index + 1:00}-{variant + 1}.wav");
        using var bytes = new MemoryStream(44 + count * 2);
        using var writer = new BinaryWriter(bytes, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + count * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);

        ReadOnlySpan<int> scale = [0, 7, 10, 12, 15, 10, 7, 3];
        var waveforms = new List<MusicFileWaveform>(duration * 10);
        var beatLength = 60d / (82 + index * 4 + variant * 7);
        float lastRms = 0;
        for (var offset = 0; offset < count; offset += blockSize)
        {
            token.ThrowIfCancellationRequested();
            short min = 0, max = 0;
            double squares = 0;
            for (var sample = offset; sample < offset + blockSize; sample++)
            {
                var time = (double)sample / SampleRate;
                var beat = time / beatLength;
                var position = beat % 1;
                var fundamental = 110 * Math.Pow(2, (index % 6 + scale[((int)(beat * 2) + variant) % scale.Length]) / 12d);
                var pluck = Math.Sin(Math.Tau * fundamental * time) * Math.Exp(-4 * (beat * 2 % 1));
                var bass = Math.Sin(Math.Tau * fundamental / 2 * time) * Math.Exp(-2 * position);
                var kick = Math.Sin(Math.Tau * (47 * time + .65 * Math.Sin(position * 8))) * Math.Exp(-15 * position);
                var fade = Math.Min(1, Math.Min(time * 12, (duration - time) * 4));
                var value = (short)(short.MaxValue * (.28 * pluck + .20 * bass + .13 * kick) * fade);
                writer.Write(value);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
                squares += Math.Pow((double)value / short.MaxValue, 2);
            }
            var rms = (float)Math.Sqrt(squares / blockSize);
            waveforms.Add(new()
            {
                OffsetInMilliseconds = offset / blockSize * 100, MinimumAmplitude = min, MaximumAmplitude = max,
                RMS = rms, Peak = (short)Math.Max(-min, max), Transient = Math.Max(0, rms - lastRms)
            });
            lastRms = rms;
        }
        var payload = bytes.ToArray();
        var candidate = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(candidate, payload, token);
            File.Move(candidate, path, overwrite: true);
        }
        finally { File.Delete(candidate); }

        var file = new LocalFile(new Uri(path).AbsoluteUri, Path.GetFileName(path),
            Convert.ToHexString(SHA256.HashData(payload)), payload.Length);
        return new(file, TimeSpan.FromSeconds(duration), waveforms);
    }

    private sealed record Artist(string FirstName, string LastName, string Accent,
        string First, string FirstPersian, string Second, string SecondPersian);
    private sealed record LocalFile(string Uri, string Name, string Hash, long Size);
    private sealed record TrackFile(LocalFile File, TimeSpan Duration, List<MusicFileWaveform> Waveforms);
    private sealed record ArtistFiles(LocalFile Portrait, LocalFile Cover, LocalFile Model, TrackFile First, TrackFile Second);
}
