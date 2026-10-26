using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Database.Entities;

const string MusicTitle = "Twinkle Twinkle Little Star";
const string AudioPath = @"D:\source\repos\Mizon.Windows\Mizon.Windows\Assets\TestMusic\twinkle-twinkle-little-star.ogg";
const string CoverPath = @"D:\source\repos\Mizon.Windows\Mizon.Windows\Assets\TestMusic\twinkle-twinkle-little-star.gif";
const int DurationInMilliseconds = 125_682;

if (!File.Exists(AudioPath) || !File.Exists(CoverPath))
    throw new FileNotFoundException("Download the CC0 lyric demo assets before running this seed tool.");

var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
    .UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True")
    .Options;

await using var database = new MizonDatabaseContext(options);

if (await database.Musics.AnyAsync(item => item.Title == MusicTitle))
{
    Console.WriteLine("The lyric demo music already exists; no duplicate data was inserted.");
    return;
}

var mediaKey = await database.MediaKeys.FirstOrDefaultAsync();
if (mediaKey is null)
{
    mediaKey = new() { KeyData = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
    database.MediaKeys.Add(mediaKey);
}

var encryptionType = await database.MediaEncryptionTypes.FirstOrDefaultAsync(item => item.Title == "None");
if (encryptionType is null)
{
    encryptionType = new() { Id = (byte)((await database.MediaEncryptionTypes.MaxAsync(item => (byte?)item.Id) ?? 0) + 1), Title = "None" };
    database.MediaEncryptionTypes.Add(encryptionType);
}

var storageType = await database.MediaStorageTypes.FirstOrDefaultAsync(item => item.Title == "Local");
if (storageType is null)
{
    storageType = new() { Id = (byte)((await database.MediaStorageTypes.MaxAsync(item => (byte?)item.Id) ?? 0) + 1), Title = "Local" };
    database.MediaStorageTypes.Add(storageType);
}

var language = await database.Languages.FirstOrDefaultAsync(item => item.Title == "English");
if (language is null)
{
    language = new() { Id = (byte)((await database.Languages.MaxAsync(item => (byte?)item.Id) ?? 0) + 1), Title = "English", PersianTitle = "انگلیسی" };
    database.Languages.Add(language);
}

var coverFormat = await database.ImageFormats.FirstOrDefaultAsync(item => item.Title == "gif");
if (coverFormat is null)
{
    coverFormat = new() { Id = (byte)((await database.ImageFormats.MaxAsync(item => (byte?)item.Id) ?? 0) + 1), Title = "gif" };
    database.ImageFormats.Add(coverFormat);
}

await database.SaveChangesAsync();

var music = new Music
{
    Title = MusicTitle,
    PersianTitle = "چشمک بزن، ستارهٔ کوچک",
    ReleaseDateTime = new DateTimeOffset(1806, 1, 1, 0, 0, 0, TimeSpan.Zero),
    IsActive = true,
};

var audioMedia = CreateLocalMedia(AudioPath, mediaKey.Id, encryptionType.Id, storageType.Id);
var coverMedia = CreateLocalMedia(CoverPath, mediaKey.Id, encryptionType.Id, storageType.Id);
music.MusicFiles.Add(new MusicFile { Media = audioMedia, Duration = TimeSpan.FromMilliseconds(DurationInMilliseconds) });
music.MusicCover = new MusicCover
{
    Media = coverMedia,
    ImageFormatId = coverFormat.Id,
    Width = 0,
    Height = 0,
    Blurhash = string.Empty,
    PrimaryAccentColor = "#3567A7",
};
music.MusicLanguages.Add(new MusicLanguage { LanguageId = language.Id });

var lyric = new MusicLyric { LanguageId = language.Id };
var lines = new[]
{
    "Twinkle twinkle little star",
    "How I wonder what you are",
    "Up above the world so high",
    "Like a diamond in the sky",
    "Twinkle twinkle little star",
    "How I wonder what you are",
    "When the blazing sun is gone",
    "When he nothing shines upon",
    "Then you show your little light",
    "Twinkle twinkle all the night",
    "Then the traveller in the dark",
    "Thanks you for your tiny spark",
    "He could not see which way to go",
    "If you did not twinkle so",
    "Twinkle twinkle little star",
    "How I wonder what you are",
};

// This CC0 recording is used as an interaction fixture. Timings are evenly
// distributed across its known duration so every word-level UI state can be tested.
// Production ingestion should replace these with forced-alignment output.
for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
{
    var lineStart = (int)Math.Round(lineIndex * (double)DurationInMilliseconds / lines.Length);
    var lineEnd = (int)Math.Round((lineIndex + 1) * (double)DurationInMilliseconds / lines.Length);
    var line = new MusicLyricLine
    {
        LineOrder = lineIndex,
        StartTimeInMilliseconds = lineStart,
        EndTimeInMilliseconds = lineEnd,
        Text = lines[lineIndex],
    };

    var words = lines[lineIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
    {
        line.MusicLyricLineWords.Add(new()
        {
            WordOrder = wordIndex,
            StartTimeInMilliseconds = lineStart + (int)Math.Round(wordIndex * (double)(lineEnd - lineStart) / words.Length),
            EndTimeInMilliseconds = lineStart + (int)Math.Round((wordIndex + 1) * (double)(lineEnd - lineStart) / words.Length),
            Text = words[wordIndex],
        });
    }

    lyric.MusicLyricLines.Add(line);
}

music.MusicLyrics.Add(lyric);
database.Musics.Add(music);
await database.SaveChangesAsync();

var musicFile = music.MusicFiles.Single();
var waveform = new List<MusicFileWaveform>(252);
for (var index = 0; index < 252; index++)
{
    var phase = index / 251d * Math.PI * 14d;
    var amplitude = 0.18d + (Math.Abs(Math.Sin(phase)) * 0.75d);
    var peak = (short)(amplitude * short.MaxValue);
    waveform.Add(new()
    {
        MusicFileId = musicFile.Id,
        OffsetInMilliseconds = index * 500,
        MinimumAmplitude = (short)-peak,
        MaximumAmplitude = peak,
        Peak = peak,
        RMS = (float)(amplitude * 0.707d),
        Transient = index == 0 ? 0 : (float)Math.Abs(Math.Sin(phase) - Math.Sin(phase - 0.35d)),
        IsClipping = false,
    });
}

database.MusicFileWaveforms.AddRange(waveform);
await database.SaveChangesAsync();
Console.WriteLine($"Inserted {MusicTitle} with public id {music.PublicId}.");

static Media CreateLocalMedia(string path, int mediaKeyId, byte encryptionTypeId, byte storageTypeId)
{
    var bytes = File.ReadAllBytes(path);
    return new()
    {
        StoragePath = new Uri(path).AbsoluteUri,
        EncryptionIV = RandomNumberGenerator.GetBytes(16),
        MediaKeyId = mediaKeyId,
        MediaEncryptionTypeId = encryptionTypeId,
        MediaStorageTypeId = storageTypeId,
        TotalChunks = 1,
        ChunkSize = bytes.Length,
        FileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        FileName = Path.GetFileName(path),
        FileSize = bytes.LongLength,
    };
}
