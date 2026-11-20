using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Mizon.Database;
using Mizon.Database.Entities;
using Mizon.Server;
using Mizon.Server.Logic.Operations.Musics;
using Mizon.Server.Logic.Requests.Musics;
using Mizon.Server.Logic.Responses.Musics;

internal static class MusicDiscussionSqlChecks
{
    internal static async Task RunAsync()
    {
        var fixture = "Mizon_MusicDiscussion_Check_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<MizonDatabaseContext>().UseSqlServer(
            $"Server=localhost;Database={fixture};Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        await using var database = new MizonDatabaseContext(options);
        try
        {
            await ApplyDiscussionMigrationAsync(database, token);

            var track = new Music { PublicId = Guid.NewGuid(), Title = "Synthetic Track", IsActive = true };
            var other = new Music { PublicId = Guid.NewGuid(), Title = "Unrelated Track", IsActive = true };
            var artist = new Person { PublicId = Guid.NewGuid(), FirstName = "Synthetic", LastName = "Artist" };
            var viewer = new Client { PublicId = Guid.NewGuid(), FirstName = "Synthetic", LastName = "Viewer" };
            var platform = new PlatformVersion
            {
                PublicId = Guid.NewGuid(), Version = "1",
                Platform = new() { PublicId = Guid.NewGuid(), Title = "Windows" }
            };
            var session = new ClientSession
            {
                Client = viewer, PublicId = Guid.NewGuid(), PlatformVersion = platform, OsVersion = "11",
                DeviceIdentifier = "fixture", DeviceName = "fixture", DeviceModel = "fixture", DeviceUsername = "fixture",
                RefreshTokenHash = "fixture", LastSeenDateTime = DateTimeOffset.UtcNow
            };
            var audio = new MusicFile
            {
                PublicId = Guid.NewGuid(), Music = track, Duration = TimeSpan.FromMinutes(3),
                Media = new()
                {
                    PublicId = Guid.NewGuid(), FileName = "fixture.wav", StoragePath = "file:///fixture.wav",
                    FileHash = new string('B', 64), EncryptionIV = new byte[16], ChunkSize = 64, TotalChunks = 1, FileSize = 64,
                    MediaKey = new() { PublicId = Guid.NewGuid(), KeyData = "fixture" },
                    MediaStorageType = new() { Id = 203, Title = "DiscussionStorage", PublicId = Guid.NewGuid() },
                    MediaEncryptionType = new() { Id = 203, Title = "DiscussionEncryption", PublicId = Guid.NewGuid() }
                }
            };
            database.AddRange(artist, session, audio, other);
            await database.SaveChangesAsync(token);
            var metadata = new RequestMetadata { ClientId = viewer.Id, SessionPublicId = session.PublicId };
            var create = new MusicCommentCreateOperation(database);
            var comments = new MusicCommentsOperation(database);
            var vote = new MusicCommentVoteOperation(database);
            var lookup = new MusicCommentTagSearchOperation(database);
            var position = TimeSpan.FromMilliseconds(12637);
            var original = await Post(new() { MusicPublicId = track.PublicId, Text = " at a time ", Timestamp = position,
                Tags = [new("person", artist.PublicId), new("client", viewer.PublicId), new("music", track.PublicId)] });
            var untimed = await Post(new() { MusicPublicId = track.PublicId, Text = "General comment" });
            var reply = await Post(new() { MusicPublicId = track.PublicId, Text = "A reply", ParentMusicCommentPublicId = original });
            var nested = await Post(new() { MusicPublicId = track.PublicId, Text = "A nested reply", ParentMusicCommentPublicId = reply });
            Require(original != Guid.Empty && untimed != Guid.Empty && reply != Guid.Empty && nested != Guid.Empty, "all four valid comments are saved");
            Require(await Post(new() { MusicPublicId = other.PublicId, Text = "Wrong track", ParentMusicCommentPublicId = original }) == Guid.Empty,
                "cross-track replies are rejected");
            Require(await Post(new() { MusicPublicId = track.PublicId, Text = "Out of range", Timestamp = TimeSpan.FromHours(1) }) == Guid.Empty,
                "the stored audio duration bounds a timed comment");
            Require(await Post(new() { MusicPublicId = track.PublicId, Text = "Invalid tag", Tags = [new("person", Guid.NewGuid())] }) == Guid.Empty,
                "a tag must resolve in its declared domain");

            var first = await Read(new() { MusicPublicId = track.PublicId, PageSize = 1 });
            var next = await Read(new() { MusicPublicId = track.PublicId, PageSize = 1, After = first.Next });
            Require(first.MusicComments.Single().PublicId == untimed && first.Next == untimed &&
                next.MusicComments.Single().PublicId == original && next.Next is null, "keyset pages contain only roots without duplicates");
            var timed = await Read(new() { MusicPublicId = track.PublicId, PageSize = 20, TimedOnly = true });
            Require(timed.MusicComments.Single().Timestamp == position, "waveform loads only timed comments without losing precision");
            var exact = (await Read(new() { MusicPublicId = track.PublicId, MusicCommentPublicId = original })).MusicComments.Single();
            Require(exact is { Platform: "Windows", Replies: 1, Text: "at a time" } && exact.Tags.Count == 3,
                "exact read resolves platform, canonical tags and reply count");
            var targetedReply = (await Read(new() { MusicPublicId = track.PublicId, MusicCommentPublicId = nested })).MusicComments.Single();
            Require(targetedReply.ParentMusicCommentPublicId == reply, "a nested link resolves its actual parent");
            Require((await Read(new() { MusicPublicId = track.PublicId, ParentMusicCommentPublicId = original, PageSize = 4 }))
                .MusicComments.Single().PublicId == reply, "thread pages return immediate replies");
            Require((await Read(new() { MusicPublicId = other.PublicId, MusicCommentPublicId = original })).MusicComments.Count == 0,
                "an exact link cannot resolve a different track's comment");

            Require((await Vote(original, 1)) is { Likes: 1, Dislikes: 0, MyVote: 1 } &&
                (await Vote(original, 1)) is { Likes: 1, Dislikes: 0, MyVote: 1 }, "repeated likes do not duplicate a vote");
            var liked = await Read(new() { MusicPublicId = track.PublicId, PageSize = 1, Sort = "likes" });
            Require(liked.MusicComments.Single().PublicId == original &&
                (await Read(new() { MusicPublicId = track.PublicId, PageSize = 1, Sort = "likes", After = liked.Next }))
                    .MusicComments.Single().PublicId == untimed, "like ordering and its cursor agree");
            Require((await Vote(original, -1)) is { Likes: 0, Dislikes: 1, MyVote: -1 } &&
                (await Read(new() { MusicPublicId = track.PublicId, PageSize = 2, Sort = "dislikes" })).MusicComments[0].PublicId == original,
                "a dislike replaces the like and participates in sorting");
            Require((await Vote(original, 0)) is { Likes: 0, Dislikes: 0, MyVote: 0 }, "clearing a vote removes it");
            var matches = await lookup.HandleAsync(new() { Content = new() { Type = "person", Query = "Synth" } }, metadata, token);
            Require(matches.Content?.Tags.Single().PublicId == artist.PublicId, "tag search resolves the correct public ID");
            Console.WriteLine("PASS Music discussion migration, posting, timing, nested replies, tags, exact links, vote transitions and pagination on SQL Server.");

            async Task<Guid> Post(MusicCommentCreateRequest input) =>
                (await create.HandleAsync(new() { Content = input }, metadata, token)).Content?.MusicCommentPublicId ?? Guid.Empty;
            async Task<MusicCommentsResponse> Read(MusicCommentsRequest input) =>
                (await comments.HandleAsync(new() { Content = input }, metadata, token)).Content ?? throw new InvalidOperationException("Discussion read failed.");
            async Task<MusicCommentVoteResponse?> Vote(Guid id, int desired) =>
                (await vote.HandleAsync(new() { Content = new() { MusicPublicId = track.PublicId, MusicCommentPublicId = id, Vote = desired } }, metadata, token)).Content;
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static async Task ApplyDiscussionMigrationAsync(MizonDatabaseContext database, CancellationToken token)
    {
        var assembly = database.GetService<IMigrationsAssembly>();
        var discussion = assembly.Migrations.Single(entry => entry.Key.EndsWith("_AddMusicCommentDiscussion", StringComparison.Ordinal));
        var previous = assembly.Migrations.Where(entry => string.CompareOrdinal(entry.Key, discussion.Key) < 0).Last();
        var provider = database.Database.ProviderName!;
        var initializer = database.GetService<IModelRuntimeInitializer>();
        var baseline = initializer.Initialize(assembly.CreateMigration(previous.Value, provider).TargetModel, designTime: true);
        var migration = assembly.CreateMigration(discussion.Value, provider);
        var model = initializer.Initialize(migration.TargetModel, designTime: true);
        var generator = database.GetService<IMigrationsSqlGenerator>();
        var baselineOperations = database.GetService<IMigrationsModelDiffer>().GetDifferences(null, baseline.GetRelationalModel());
        await database.GetService<IRelationalDatabaseCreator>().CreateAsync(token);
        await ExecuteAsync(generator.Generate(baselineOperations, baseline));
        await ExecuteAsync(generator.Generate(migration.UpOperations, model));
        await ExecuteAsync(generator.Generate(migration.DownOperations, baseline));
        await ExecuteAsync(generator.Generate(migration.UpOperations, model));

        async Task ExecuteAsync(IReadOnlyList<MigrationCommand> commands)
        {
            foreach (var command in commands)
                await database.Database.ExecuteSqlRawAsync(command.CommandText, token);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
