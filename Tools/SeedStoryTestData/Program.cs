using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Database.Entities;

const string assetsPath = @"D:\source\repos\Mizon.Windows\Mizon.Windows\Assets";
var imageFiles = Enumerable.Range(1, 4)
    .Select(index => Path.Combine(assetsPath, $"image{index}.jpeg"))
    .Concat(Enumerable.Range(0, 6).Select(index => Path.Combine(assetsPath, index == 0 ? "photo.jpg" : $"photo{index + 1}.jpg")))
    .ToArray();
var videoFile = Path.Combine(assetsPath, "story-test-video.mp4");

if (imageFiles.Any(file => !File.Exists(file)) || !File.Exists(videoFile))
    throw new FileNotFoundException("Story test assets were not found.");

var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
    .UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True")
    .Options;

await using var database = new MizonDatabaseContext(options);
var music = await database.Musics.OrderBy(x => x.Id).FirstOrDefaultAsync();
if (music is null)
    throw new InvalidOperationException("No music exists to tag in the test stories.");

var mediaKey = await database.MediaKeys.FirstOrDefaultAsync();
if (mediaKey is null)
{
    mediaKey = new MediaKey { KeyData = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
    database.MediaKeys.Add(mediaKey);
}

var encryptionType = await database.MediaEncryptionTypes.FirstOrDefaultAsync();
if (encryptionType is null)
{
    var nextId = await database.MediaEncryptionTypes.Select(x => (byte?)x.Id).MaxAsync() ?? 0;
    encryptionType = new MediaEncryptionType { Id = (byte)(nextId + 1), Title = "None" };
    database.MediaEncryptionTypes.Add(encryptionType);
}

var storageType = await database.MediaStorageTypes.FirstOrDefaultAsync();
if (storageType is null)
{
    var nextId = await database.MediaStorageTypes.Select(x => (byte?)x.Id).MaxAsync() ?? 0;
    storageType = new MediaStorageType { Id = (byte)(nextId + 1), Title = "Local" };
    database.MediaStorageTypes.Add(storageType);
}

var imageFormat = await database.ImageFormats.FirstOrDefaultAsync(x => x.Title == "jpeg");
if (imageFormat is null)
{
    var nextId = await database.ImageFormats.Select(x => (byte?)x.Id).MaxAsync() ?? 0;
    imageFormat = new ImageFormat { Id = (byte)(nextId + 1), Title = "jpeg" };
    database.ImageFormats.Add(imageFormat);
}

await database.SaveChangesAsync();

var imageStoryTypeId = await database.StoryContentTypes.Where(x => x.Title == "Image").Select(x => x.Id).SingleAsync();
var videoStoryTypeId = await database.StoryContentTypes.Where(x => x.Title == "Video").Select(x => x.Id).SingleAsync();
var musicWidgetTypeId = await database.StoryWidgetTypes.Where(x => x.Title == "MusicTag").Select(x => x.Id).SingleAsync();
var musicWidgetStyleId = await database.StoryWidgetStyles
    .Where(x => x.StoryWidgetTypeId == musicWidgetTypeId)
    .OrderBy(x => x.Id)
    .Select(x => x.Id)
    .FirstAsync();

var firstNames = new[] { "آرین", "نیلوفر", "سام", "رها", "کیان", "مهسا", "آوا", "پارسا", "سارا", "مانی" };
var lastNames = new[] { "مرادی", "احمدی", "رضایی", "نیک‌خواه", "کریمی", "صادقی", "یزدانی", "میرزایی", "حسینی", "فلاح" };
var inserted = 0;

for (var index = 0; index < 10; index++)
{
    var title = $"Story test #{index + 1:00}";
    if (await database.Stories.AnyAsync(story => story.Title == title))
        continue;

    var username = $"story_test_{index + 1:00}";
    var client = await database.Clients
        .Where(item => item.ClientUsernames.Any(usernameItem => usernameItem.Username == username))
        .FirstOrDefaultAsync();
    if (client is null)
    {
        client = new Client { FirstName = firstNames[index], LastName = lastNames[index], NickName = $"تستر {index + 1}" };
        client.ClientUsernames.Add(new ClientUsername { Username = username, IsActive = true });
    }

    var profileSource = imageFiles[index];
    var profileMedia = CreateMedia(profileSource, mediaKey.Id, encryptionType.Id, storageType.Id, $"profile-{index + 1}");
    client.ClientProfiles.Add(new ClientProfile
    {
        Media = profileMedia,
        ImageFormatId = imageFormat.Id,
        Blurhash = string.Empty,
        Width = 0,
        Height = 0,
        PrimaryAccentColor = "#3B82F6",
        IsDefault = true,
    });

    var isVideo = index % 2 == 1;
    var contentSource = isVideo ? videoFile : imageFiles[index];
    var contentMedia = CreateMedia(contentSource, mediaKey.Id, encryptionType.Id, storageType.Id, $"story-content-{index + 1}");
    var previewMedia = CreateMedia(imageFiles[(index + 3) % imageFiles.Length], mediaKey.Id, encryptionType.Id, storageType.Id, $"story-preview-{index + 1}");
    var story = new Story
    {
        Client = client,
        ContentMedia = contentMedia,
        PreviewMedia = previewMedia,
        StoryContentTypeId = isVideo ? videoStoryTypeId : imageStoryTypeId,
        Title = title,
        ExpireDateTime = index == 9 ? null : DateTimeOffset.UtcNow.AddDays(7),
    };
    story.StoryWidgets.Add(new StoryWidget
    {
        StoryWidgetTypeId = musicWidgetTypeId,
        StoryWidgetStyleId = musicWidgetStyleId,
        X = 0.1f,
        Y = 0.72f,
        Width = 0.8f,
        Height = 0.12f,
        Rotation = 0,
        ZIndex = 1,
        MusicWidget = new StoryMusicWidget { MusicId = music.Id },
    });

    database.Stories.Add(story);
    await database.SaveChangesAsync();
    inserted++;
}

Console.WriteLine($"Inserted {inserted} test stories tagged with music '{music.Title}' ({music.PublicId}).");
var storyCount = await database.Stories.CountAsync(story => story.Title != null && story.Title.StartsWith("Story test #"));
var videoCount = await database.Stories
    .Where(story => story.Title != null && story.Title.StartsWith("Story test #"))
    .Where(story => story.StoryContentTypeId == videoStoryTypeId)
    .CountAsync();
var clientCount = await database.Clients.CountAsync(client => client.ClientUsernames.Any(username => username.Username.StartsWith("story_test_")));
Console.WriteLine($"Verified: {clientCount} clients, {storyCount} stories, {videoCount} videos, {storyCount - videoCount} images.");

static Media CreateMedia(string sourcePath, int mediaKeyId, byte encryptionTypeId, byte storageTypeId, string logicalName)
{
    var file = new FileInfo(sourcePath);
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
    return new Media
    {
        StoragePath = new Uri(sourcePath).AbsoluteUri,
        EncryptionIV = RandomNumberGenerator.GetBytes(16),
        MediaKeyId = mediaKeyId,
        MediaEncryptionTypeId = encryptionTypeId,
        MediaStorageTypeId = storageTypeId,
        TotalChunks = 1,
        ChunkSize = 256 * 1024,
        FileHash = $"{hash}-{logicalName}",
        FileName = Path.GetFileName(sourcePath),
        FileSize = file.Length,
    };
}
