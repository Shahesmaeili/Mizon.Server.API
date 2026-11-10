using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mizon.Database;
using Mizon.Database.Entities;
using Mizon.Server;
using Mizon.Server.Logic.Operations.Search;
using Mizon.Server.Logic.Operations.Stories;
using Mizon.Server.Logic.Requests.Search;
using Mizon.Server.Logic.Requests.Stories;

internal static class StoryCreateChecks
{
    public static async Task RunAsync(DbContextOptions<MizonDatabaseContext> options)
    {
        // Both guards are installed before any operation: SaveChanges never reaches
        // SQL, and every command must independently be a SELECT.
        var reads = new SelectOnlyInterceptor();
        var stopSave = new StopSaveInterceptor();
        var guardedOptions = new DbContextOptionsBuilder<MizonDatabaseContext>(options)
            .AddInterceptors(reads, stopSave).Options;
        await using var fixtures = new MizonDatabaseContext(guardedOptions);
        var client = await fixtures.Clients.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync();
        var music = await fixtures.Musics.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync();
        var media = await fixtures.Medias.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync();
        var contentType = await fixtures.StoryContentTypes.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync();
        var styles = await fixtures.StoryWidgetStyles.AsNoTracking()
            .Where(x => x.StoryWidgetTypeId == 1 || x.StoryWidgetTypeId == 2)
            .OrderBy(x => x.Id)
            .Select(x => new { x.PublicId, x.Id, x.StoryWidgetTypeId, TypePublicId = x.StoryWidgetType!.PublicId })
            .ToListAsync();
        var musicStyle = styles.FirstOrDefault(x => x.StoryWidgetTypeId == 2);
        var clientStyle = styles.FirstOrDefault(x => x.StoryWidgetTypeId == 1);
        if (client is null || music is null || media is null || contentType is null
            || musicStyle is null || clientStyle is null)
        {
            Console.WriteLine("SKIP StoryCreate regression: media/client/music/content-type/music-style/client-style fixture missing.");
            return;
        }

        StoryWidgetCreateRequest MusicWidget() => new()
        {
            StoryWidgetTypePublicId = musicStyle.TypePublicId,
            StoryWidgetStylePublicId = musicStyle.PublicId,
            MusicPublicId = music.PublicId,
            X = 0.25f, Y = 0.75f, Width = 0.4f, Height = 0.1f, Rotation = 17, ZIndex = 2,
        };
        StoryWidgetCreateRequest ClientWidget() => new()
        {
            StoryWidgetTypePublicId = clientStyle.TypePublicId,
            StoryWidgetStylePublicId = clientStyle.PublicId,
            ClientPublicId = client.PublicId,
            X = 0.5f, Y = 0.2f, Width = 0.3f, Height = 0.15f, Rotation = -11, ZIndex = 1,
        };
        StoryCreateRequest Request(params StoryWidgetCreateRequest[] widgets) => new()
        {
            ContentMediaPublicId = media.PublicId,
            PreviewMediaPublicId = media.PublicId,
            StoryContentTypePublicId = contentType.PublicId,
            Title = "Regression check; never persisted",
            Widgets = [.. widgets],
        };
        var metadata = new RequestMetadata { ClientId = client.Id };

        async Task ExpectSaveAsync(string label, StoryCreateRequest input, int expectedQueries)
        {
            await using var database = new MizonDatabaseContext(guardedOptions);
            reads.Count = 0;
            try
            {
                await new StoryCreateOperation(database).HandleAsync(new() { Content = input }, metadata, CancellationToken.None);
                throw new InvalidOperationException($"{label}: operation did not reach guarded SaveChanges.");
            }
            catch (SaveBlockedException)
            {
                var story = database.ChangeTracker.Entries<Story>().Single().Entity;
                Require(story.ClientId == client.Id && story.ContentMediaId == media.Id
                    && story.PreviewMediaId == media.Id && story.StoryContentTypeId == contentType.Id,
                    label + ": root references");
                Require(story.Title == input.Title && story.ExpireDateTime == input.ExpireDateTime,
                    label + ": root values");
                Require(story.StoryWidgets.Count == input.Widgets.Count, label + ": tag count");
                for (var index = 0; index < input.Widgets.Count; index++)
                {
                    var actual = story.StoryWidgets[index];
                    var expected = input.Widgets[index];
                    Require(actual.X == expected.X && actual.Y == expected.Y
                        && actual.Width == expected.Width && actual.Height == expected.Height
                        && actual.Rotation == expected.Rotation && actual.ZIndex == expected.ZIndex,
                        label + ": widget transform/order");
                    if (expected.MusicPublicId.HasValue)
                        Require(actual.StoryWidgetTypeId == 2 && actual.StoryWidgetStyleId == musicStyle.Id
                            && actual.MusicWidget?.MusicId == music.Id && actual.ClientWidget is null,
                            label + ": music tag");
                    else
                        Require(actual.StoryWidgetTypeId == 1 && actual.StoryWidgetStyleId == clientStyle.Id
                            && actual.ClientWidget?.ClientId == client.Id && actual.MusicWidget is null,
                            label + ": client tag");
                }
                Require(database.ChangeTracker.Entries().All(x => x.State == EntityState.Added),
                    label + ": lookups remain untracked");
                Require(reads.Count == expectedQueries, $"{label}: expected {expectedQueries} reads, got {reads.Count}");
                Console.WriteLine($"PASS StoryCreate {label}; {reads.Count} SELECT commands; save blocked before SQL.");
            }
        }

        async Task ExpectErrorAsync(string label, int code, params StoryWidgetCreateRequest[] widgets)
        {
            await using var database = new MizonDatabaseContext(guardedOptions);
            var response = await new StoryCreateOperation(database).HandleAsync(
                new BaseApiRequest<StoryCreateRequest> { Content = Request(widgets) }, metadata, CancellationToken.None);
            Require(response.Error?.Code == code, $"{label}: expected error {code}, got {response.Error?.Code}");
            Require(!database.ChangeTracker.HasChanges(), label + ": rejected request did not attach partial story");
            Console.WriteLine($"PASS StoryCreate {label}; error {code}.");
        }

        await ExpectSaveAsync("empty widgets", Request(), 3);
        await ExpectSaveAsync("one music widget", Request(MusicWidget()), 6);
        await ExpectSaveAsync("repeated mixed widgets", Request(MusicWidget(), ClientWidget(), MusicWidget(), ClientWidget()), 7);
        var manyWidgets = Enumerable.Range(0, 24).Select(index => index % 2 == 0 ? MusicWidget() : ClientWidget()).ToArray();
        await ExpectSaveAsync("24 mixed widgets keep bounded queries", Request(manyWidgets), 7);

        var invalidWidth = MusicWidget();
        invalidWidth.Width = 0;
        await ExpectErrorAsync("invalid size", 11002, invalidWidth);
        var missingType = MusicWidget();
        missingType.StoryWidgetTypePublicId = Guid.NewGuid();
        await ExpectErrorAsync("missing type", 11002, missingType);
        var missingStyle = MusicWidget();
        missingStyle.StoryWidgetStylePublicId = Guid.NewGuid();
        await ExpectErrorAsync("missing style", 11002, missingStyle);
        var mismatchedStyle = MusicWidget();
        mismatchedStyle.StoryWidgetStylePublicId = clientStyle.PublicId;
        await ExpectErrorAsync("mismatched type/style", 11002, mismatchedStyle);
        var missingMusic = MusicWidget();
        missingMusic.MusicPublicId = Guid.NewGuid();
        await ExpectErrorAsync("missing music", 10000, missingMusic);
        var missingClient = ClientWidget();
        missingClient.ClientPublicId = Guid.NewGuid();
        await ExpectErrorAsync("missing client", 11003, missingClient);
        var missingTarget = MusicWidget();
        missingTarget.MusicPublicId = null;
        await ExpectErrorAsync("missing tag target", 11002, missingTarget);
        await ExpectErrorAsync("missing music precedes later invalid size", 10000, missingMusic, invalidWidth);
        await ExpectErrorAsync("invalid size precedes later missing music", 11002, invalidWidth, missingMusic);

        // EF 10.0.5 falls back to OPENJSON for a collection exceeding SQL
        // Server's parameter budget. Exercise all four independent lookups,
        // retaining the first widget's missing-music error despite later bad
        // type/style references. Repeated IDs alone would not test this limit.
        var largeWidgets = Enumerable.Range(0, 2200).Select(index =>
        {
            var widget = MusicWidget();
            widget.MusicPublicId = Guid.NewGuid();
            widget.ClientPublicId = Guid.NewGuid();
            if (index > 0)
            {
                widget.StoryWidgetTypePublicId = Guid.NewGuid();
                widget.StoryWidgetStylePublicId = Guid.NewGuid();
            }
            return widget;
        }).ToArray();
        reads.MaximumParameters = 0;
        reads.OpenJsonCommands = 0;
        await ExpectErrorAsync("2200 distinct references preserve first error", 10000, largeWidgets);
        Require(reads.MaximumParameters <= 2098, "large reference query exceeded SQL Server's parameter budget");
        Require(reads.OpenJsonCommands == 4, "large reference queries did not use expected EF 10 OPENJSON fallback");
        Console.WriteLine($"PASS StoryCreate large-collection translation; {reads.OpenJsonCommands} OPENJSON commands; max {reads.MaximumParameters} parameters.");

        await using var cancelledDatabase = new MizonDatabaseContext(guardedOptions);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await new StoryCreateOperation(cancelledDatabase).HandleAsync(
                new() { Content = Request(MusicWidget()) }, metadata, cancelled.Token);
            throw new InvalidOperationException("StoryCreate pre-cancellation was ignored.");
        }
        catch (OperationCanceledException)
        {
            Require(!cancelledDatabase.ChangeTracker.HasChanges(), "cancelled story left pending changes");
            Console.WriteLine("PASS StoryCreate pre-cancellation; no pending writes.");
        }
    }

    public static async Task RunSearchAsync(DbContextOptions<MizonDatabaseContext> options)
    {
        var guardedOptions = new DbContextOptionsBuilder<MizonDatabaseContext>(options)
            .AddInterceptors(new SelectOnlyInterceptor(), new StopSaveInterceptor()).Options;
        var operation = new GlobalSearchOperation(guardedOptions);
        // Two LIKE wildcards match existing nonempty names without relying on a
        // particular fixture language. They retain the current search semantics.
        var request = new GlobalSearchRequest { Query = "%_" };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var enumerator = operation.SearchAsync(request, timeout.Token).GetAsyncEnumerator();
        var hasResult = false;
        try
        {
            hasResult = await enumerator.MoveNextAsync();
        }
        finally
        {
            await enumerator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        Console.WriteLine(hasResult
            ? "PASS GlobalSearch early disposal stops both producers without blocking."
            : "SKIP GlobalSearch early-result disposal: no searchable rows; empty stream disposal passed.");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await foreach (var _ in operation.SearchAsync(request, cancelled.Token)) { }
            throw new InvalidOperationException("GlobalSearch pre-cancellation was ignored.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("PASS GlobalSearch pre-cancellation disposes producers.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class SaveBlockedException : Exception { }

    private sealed class StopSaveInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw new SaveBlockedException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new SaveBlockedException();
    }

    private sealed class SelectOnlyInterceptor : DbCommandInterceptor
    {
        private int _count;
        public int Count { get => Volatile.Read(ref _count); set => Volatile.Write(ref _count, value); }
        public int MaximumParameters { get; set; }
        public int OpenJsonCommands { get; set; }

        private void Check(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Regression checks permit SELECT commands only.");
            MaximumParameters = Math.Max(MaximumParameters, command.Parameters.Count);
            if (command.CommandText.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase))
                OpenJsonCommands++;
            Interlocked.Increment(ref _count);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result) { Check(command); return result; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result) => throw new InvalidOperationException("Non-query commands are disabled in regression checks.");

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Non-query commands are disabled in regression checks.");

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result) { Check(command); return result; }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Check(command); return ValueTask.FromResult(result); }
    }
}
