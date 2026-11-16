using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Mizon.Database;
using Mizon.Database.Entities;
using Mizon.Server;
using Mizon.Server.Logic.Operations.Persons;

internal static class PersonProfileSqlChecks
{
    // This repository's historical migration chain assumes an older schema.
    // Create the previous schema from its migration model, and apply the new
    // migration's generated DDL only to a fresh, isolated database.
    private static async Task CreateAndMigrateFixtureAsync(MizonDatabaseContext database, CancellationToken token)
    {
        var assembly = database.GetService<IMigrationsAssembly>();
        var artist = assembly.Migrations.Single(entry => entry.Key.EndsWith("_AddPersonProfileMediaAndClientLink", StringComparison.Ordinal));
        var previous = assembly.Migrations.Where(entry => string.CompareOrdinal(entry.Key, artist.Key) < 0).Last();
        var previousMigration = assembly.CreateMigration(previous.Value, database.Database.ProviderName!);
        var artistMigration = assembly.CreateMigration(artist.Value, database.Database.ProviderName!);
        var previousModel = database.GetService<IModelRuntimeInitializer>().Initialize(previousMigration.TargetModel, designTime: true);
        var model = database.GetService<IModelRuntimeInitializer>().Initialize(artistMigration.TargetModel, designTime: true);
        var previousSchema = database.GetService<IMigrationsModelDiffer>().GetDifferences(null, previousModel.GetRelationalModel());
        var sql = database.GetService<IMigrationsSqlGenerator>();

        await database.GetService<IRelationalDatabaseCreator>().CreateAsync(token);
        await Execute(sql.Generate(previousSchema, previousModel));
        await Execute(sql.Generate(artistMigration.UpOperations, model));
        await Execute(sql.Generate(artistMigration.DownOperations, previousModel));
        await Execute(sql.Generate(artistMigration.UpOperations, model));

        var featured = assembly.Migrations.Single(entry => entry.Key.EndsWith("_AddFeaturedPersons", StringComparison.Ordinal));
        var featuredMigration = assembly.CreateMigration(featured.Value, database.Database.ProviderName!);
        var featuredModel = database.GetService<IModelRuntimeInitializer>().Initialize(featuredMigration.TargetModel, designTime: true);
        await Execute(sql.Generate(featuredMigration.UpOperations, featuredModel));
        await Execute(sql.Generate(featuredMigration.DownOperations, model));
        await Execute(sql.Generate(featuredMigration.UpOperations, featuredModel));

        async Task Execute(IReadOnlyList<MigrationCommand> commands)
        {
            foreach (var command in commands)
                await database.Database.ExecuteSqlRawAsync(command.CommandText, token);
        }
    }

    internal static async Task RunAsync()
    {
        var databaseName = "Mizon_PersonProfile_Check_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<MizonDatabaseContext>().UseSqlServer(
            $"Server=localhost;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        await using var database = new MizonDatabaseContext(options);
        try
        {
            await CreateAndMigrateFixtureAsync(database, token);
            var viewer = new Client { PublicId = Guid.NewGuid(), FirstName = "Test", LastName = "Viewer" };
            var owner = new Client { PublicId = Guid.NewGuid(), FirstName = "Test", LastName = "Owner" };
            var privateType = await database.ClientPrivacyTypes.Where(x => x.Title == "Private").FirstOrDefaultAsync(token)
                ?? new ClientPrivacyType { Id = 203, Title = "Private", PublicId = Guid.NewGuid() };
            var target = new Person { PublicId = Guid.NewGuid(), FirstName = "Test", LastName = "Artist", LinkedClient = owner };
            var other = new Person { PublicId = Guid.NewGuid(), FirstName = "Other", LastName = "Artist" };
            target.ModelMedia = new Media
            {
                PublicId = Guid.NewGuid(), FileName = "artist.glb", StoragePath = "file:///artist.glb",
                FileHash = new string('A', 64), EncryptionIV = new byte[16],
                ChunkSize = 64, TotalChunks = 1, FileSize = 64,
                MediaKey = new() { PublicId = Guid.NewGuid(), KeyData = "fixture" },
                MediaStorageType = new() { Id = 203, Title = "ProfileTestStorage", PublicId = Guid.NewGuid() },
                MediaEncryptionType = new() { Id = 203, Title = "ProfileTestEncryption", PublicId = Guid.NewGuid() }
            };
            database.AddRange(viewer, target, other);
            var vocal = new MusicContributorType { Id = 201, Title = "ProfileTestVocal", PublicId = Guid.NewGuid() };
            var writer = new MusicContributorType { Id = 202, Title = "ProfileTestWriter", PublicId = Guid.NewGuid() };
            for (var i = 0; i < 28; i++)
            {
                var track = new Music { PublicId = Guid.NewGuid(), Title = "Test " + i, IsActive = i != 0 };
                database.Add(new MusicContributor { Music = track, Person = target, MusicContributorType = vocal });
                if (i == 1) database.Add(new MusicContributor { Music = track, Person = target, MusicContributorType = writer });
            }
            var platform = await database.PlatformVersions.FirstOrDefaultAsync(token) ?? new PlatformVersion
            {
                PublicId = Guid.NewGuid(), Version = "ProfileTest",
                Platform = new Platform { PublicId = Guid.NewGuid(), Title = "ProfileTest" }
            };
            ClientSession MakeSession(Client client) => new()
            {
                Client = client, PublicId = Guid.NewGuid(), PlatformVersion = platform, OsVersion = "11.0",
                DeviceIdentifier = "fixture", DeviceName = "fixture", DeviceModel = "fixture", DeviceUsername = "fixture",
                RefreshTokenHash = "fixture", LastSeenDateTime = DateTimeOffset.UtcNow
            };
            var viewerSessionRow = MakeSession(viewer);
            var ownerSessionRow = MakeSession(owner);
            database.AddRange(viewerSessionRow, ownerSessionRow);
            await database.SaveChangesAsync(token);
            var session = new RequestMetadata { ClientId = viewer.Id, SessionPublicId = viewerSessionRow.PublicId };
            var ownerSession = new RequestMetadata { ClientId = owner.Id, SessionPublicId = ownerSessionRow.PublicId };
            var personId = target.PublicId;
            var profile = new PersonProfileOperation(database);
            var tracks = new PersonMusicsOperation(database);
            var initial = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(initial.Content is { FollowersCount: 0, FollowingCount: 0, MusicsCount: 27, CanFollow: true }, "profile count excludes inactive and duplicate credits");
            Require(initial.Content is { ModelYawInDegrees: null } && initial.Content.ModelMediaPublicId == target.ModelMedia.PublicId,
                "unfeatured model retains the client default yaw");
            var unlinked = await profile.HandleAsync(new() { Content = new() { PersonPublicId = other.PublicId } }, session, token);
            Require(unlinked.Content is { FollowingCount: null }, "unlinked person does not publish a fabricated following count");

            var first = (await tracks.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token)).Content!;
            var second = (await tracks.HandleAsync(new() { Content = new()
            {
                PersonPublicId = personId, BeforeMusicPublicId = first.NextBeforeMusicPublicId
            } }, session, token)).Content!;
            Require(first.Musics.Count == 24 && second.Musics.Count == 3 && second.NextBeforeMusicPublicId is null
                && first.Musics.Select(x => x.PublicId).Intersect(second.Musics.Select(x => x.PublicId)).Count() == 0,
                "keyset pagination gives bounded non-overlapping pages");
            Require((await tracks.HandleAsync(new() { Content = new()
            {
                PersonPublicId = other.PublicId, BeforeMusicPublicId = first.NextBeforeMusicPublicId
            } }, session, token)).Error?.Code == 40002, "a cursor from another person is rejected");

            async Task<BaseApiResponse<Mizon.Server.Logic.Responses.Persons.PersonFollowResponse>> ChangeAsync(
                Guid id, RequestMetadata? metadata, bool following)
            {
                await using var db = new MizonDatabaseContext(options);
                return await new PersonFollowOperation(db).HandleAsync(new()
                {
                    Content = new() { PersonPublicId = id, Following = following }
                }, metadata, token);
            }
            Require((await ChangeAsync(personId, null, true)).Error?.Code == 20000, "follow requires a session");
            Require((await ChangeAsync(personId, ownerSession, true)).Error?.Code == 40003, "owner cannot follow own artist");
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ChangeAsync(personId, session, true)));
            Require(concurrent.All(result => result.Content is { FollowersCount: 1, IsFollowing: true }),
                "four concurrent duplicate follows commit one relationship");
            Require((await ChangeAsync(personId, session, false)).Content is { IsFollowing: false, FollowersCount: 0 },
                "unfollow updates the count");
            Require((await ChangeAsync(personId, session, false)).IsSuccess && (await ChangeAsync(personId, session, true)).IsSuccess,
                "repeat unfollow and refollow are safe");
            var history = await database.ClientFollowHistories.AsNoTracking()
                .OrderBy(x => x.Id).Select(x => x.ClientFollowActionType!.Title).ToListAsync(token);
            Require(history.SequenceEqual(["Follow", "Unfollow", "Follow"]), "history records exactly one row per real change");
            Require(await database.ClientFollows.CountAsync(token) == 1, "refollow reuses the relationship row");

            Require((await ChangeAsync(other.PublicId, ownerSession, true)).Content is { IsFollowing: true }, "linked client can follow a different person");
            var linked = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(linked.Content is { IsFollowing: true, FollowersCount: 1, FollowingCount: 1 }, "real outgoing count for a public linked account");
            if (database.Entry(privateType).State == EntityState.Detached) database.Add(privateType);
            owner.ClientPrivacyType = privateType;
            await database.SaveChangesAsync(token);
            var privateProfile = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(privateProfile.Content is { FollowingCount: null }, "private linked following counts are not disclosed to other accounts");
            var musicType = new FeaturedPersonType { Id = 1, Code = "music", Title = "Music" };
            var moviesType = new FeaturedPersonType { Id = 2, Code = "movies", Title = "Movies" };
            var seriesType = new FeaturedPersonType { Id = 3, Code = "series", Title = "Series" };
            database.AddRange(
                new FeaturedPerson { FeaturedPersonType = musicType, Person = target, SortOrder = 2, ModelYawInDegrees = 180 },
                new FeaturedPerson { FeaturedPersonType = musicType, Person = other, SortOrder = 1 },
                new FeaturedPerson { FeaturedPersonType = moviesType, Person = other, SortOrder = 1, ModelYawInDegrees = 45 },
                seriesType);
            var played = await database.MusicContributors.Where(x => x.PersonId == target.Id && x.Music!.Title == "Test 1")
                .Select(x => x.MusicId).FirstAsync(token);
            database.AddRange(
                new MusicPlay { ClientId = viewer.Id, MusicId = played, PlatformVersion = platform, PlaybackPublicId = Guid.NewGuid() },
                new MusicPlay { ClientId = viewer.Id, MusicId = played, PlatformVersion = platform, PlaybackPublicId = Guid.NewGuid() },
                new MusicPlay { ClientId = owner.Id, MusicId = played, PlatformVersion = platform, PlaybackPublicId = Guid.NewGuid() });
            await database.SaveChangesAsync(token);
            var featured = new FeaturedPersonsOperation(database);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var musicList = (await featured.HandleAsync(new() { Content = new() { TypeCode = " Music " } }, session, token)).Content!.Persons;
            Console.WriteLine($"Featured people first execution: {watch.Elapsed.TotalMilliseconds:F1} ms (isolated SQL fixture)");
            Require(musicList.Select(x => x.PersonPublicId).SequenceEqual([other.PublicId, target.PublicId]), "featured list honors exact editorial order");
            Require(musicList[1] is { CanFollow: true, IsFollowing: true, FollowersCount: 1, MusicsCount: 27, MonthlyListenersCount: 2 }
                && musicList[1].LatestMusics.Count == 3 && musicList[1].DailyPlays.Sum() == 3,
                "featured data excludes duplicate credits, uses actual plays, counts unique listeners and limits latest tracks");
            var moviesList = (await featured.HandleAsync(new() { Content = new() { TypeCode = "movies" } }, ownerSession, token)).Content!.Persons;
            Require(moviesList.Count == 1 && moviesList[0].PersonPublicId == other.PublicId && moviesList[0].LatestMusics.Count == 0,
                "movie list is independent and omits music-specific work");
            Require((await featured.HandleAsync(new() { Content = new() { TypeCode = "series" } }, session, token)).Content!.Persons.Count == 0,
                "empty curated lists return empty results");
            Require(!(await featured.HandleAsync(new() { Content = new() { TypeCode = "bad" } }, session, token)).IsSuccess,
                "unknown featured type is rejected");

            var movieAppearance = new FeaturedPerson
            {
                FeaturedPersonType = moviesType, Person = target, SortOrder = 2, ModelYawInDegrees = 45
            };
            database.Add(movieAppearance);
            await database.SaveChangesAsync(token);
            var curated = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(curated.Content?.ModelYawInDegrees == 180 && curated.Content.ModelYawInDegrees == musicList[1].ModelYawInDegrees,
                "profile starts at the same configured yaw as music even when a movie placement uses another angle");
            var noModel = await profile.HandleAsync(new() { Content = new() { PersonPublicId = other.PublicId } }, session, token);
            Require(noModel.Content is { ModelMediaPublicId: null, ModelYawInDegrees: null },
                "a placement without a model does not return a yaw");
            movieAppearance.FeaturedPersonType = seriesType;
            await database.SaveChangesAsync(token);
            database.Remove(await database.FeaturedPersons.SingleAsync(x => x.PersonId == target.Id
                && x.FeaturedPersonTypeId == musicType.Id, token));
            await database.SaveChangesAsync(token);
            var seriesProfile = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(seriesProfile.Content is { ModelYawInDegrees: 45 }, "profile can use a model pose from a non-music placement");

            viewerSessionRow.LogoutDateTime = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token);
            Require((await ChangeAsync(personId, session, false)).Error?.Code == 20000, "a signed out session cannot unfollow");
            var signedOut = await profile.HandleAsync(new() { Content = new() { PersonPublicId = personId } }, session, token);
            Require(signedOut.Content is { CanFollow: false, IsFollowing: false }, "an inactive session gets no personalized follow state");
            var signedOutFeatured = (await featured.HandleAsync(new() { Content = new() { TypeCode = "music" } }, session, token)).Content!.Persons;
            Require(signedOutFeatured.All(x => !x.CanFollow && !x.IsFollowing),
                "featured people does not personalize for an inactive session");
            Console.WriteLine("PASS isolated SQL Server: both migrations, profile privacy, pagination, follow history, ordered featured catalogs, model yaw and real music metrics.");
        }
        finally
        {
            await database.Database.EnsureDeletedAsync(CancellationToken.None);
        }

        static void Require(bool passed, string check)
        {
            if (!passed) throw new InvalidOperationException(check);
        }
    }
}
