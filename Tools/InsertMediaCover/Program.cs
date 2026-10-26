using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Mizon.Database;
using Mizon.Database.Entities;
using Microsoft.EntityFrameworkCore;

internal static class Program
{
    private const string ImageUrl = "https://4kwallpapers.com/images/walls/thumbs_2t/26832.jpg";
    private const int TargetMusicId = 1;

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("InsertMediaCover tool starting...");

        try
        {
            using var client = new HttpClient();
            // Some hosts block requests without a User-Agent. Set a common browser UA and accept image types.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
            var bytes = await client.GetByteArrayAsync(ImageUrl);

            var fileSize = bytes.LongLength;
            byte[] iv = RandomNumberGenerator.GetBytes(16);

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var fileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            var uri = new Uri(ImageUrl);
            var fileName = Path.GetFileName(uri.LocalPath);

            var optionsBuilder = new DbContextOptionsBuilder<MizonDatabaseContext>();
            optionsBuilder.UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True");

            using var db = new MizonDatabaseContext(optionsBuilder.Options);

            // Ensure required lookup records exist
            var mediaKey = await db.MediaKeys.FirstOrDefaultAsync() ?? new MediaKey { KeyData = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            if (mediaKey.Id == 0)
                db.MediaKeys.Add(mediaKey);

            var mediaEnc = await db.MediaEncryptionTypes.FirstOrDefaultAsync();
            if (mediaEnc is null)
            {
                mediaEnc = new MediaEncryptionType { Id = 1, Title = "None" };
                db.MediaEncryptionTypes.Add(mediaEnc);
            }

            var mediaStorage = await db.MediaStorageTypes.FirstOrDefaultAsync();
            if (mediaStorage is null)
            {
                mediaStorage = new MediaStorageType { Id = 1, Title = "Remote" };
                db.MediaStorageTypes.Add(mediaStorage);
            }

            // Image format
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var imageFormat = await db.ImageFormats.FirstOrDefaultAsync(x => x.Title == ext);
            if (imageFormat is null)
            {
                imageFormat = new ImageFormat { Id = 2, Title = ext };
                db.ImageFormats.Add(imageFormat);
            }

            await db.SaveChangesAsync();

            var media = new Media
            {
                StoragePath = ImageUrl,
                EncryptionIV = iv,
                MediaKeyId = mediaKey.Id,
                MediaEncryptionTypeId = mediaEnc.Id,
                MediaStorageTypeId = mediaStorage.Id,
                TotalChunks = 1,
                ChunkSize = bytes.Length,
                FileHash = fileHash,
                FileName = fileName,
                FileSize = fileSize
            };

            db.Medias.Add(media);
            await db.SaveChangesAsync();

            var musicCover = new MusicCover
            {
                MusicId = TargetMusicId,
                MediaId = media.Id,
                Blurhash = string.Empty,
                Width = 0,
                Height = 0,
                ImageFormatId = imageFormat.Id,
                PrimaryAccentColor = "#000000"
            };

            db.MusicCovers.Add(musicCover);
            await db.SaveChangesAsync();

            Console.WriteLine($"Inserted media id={media.Id} and music cover for music id={TargetMusicId}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }
}
