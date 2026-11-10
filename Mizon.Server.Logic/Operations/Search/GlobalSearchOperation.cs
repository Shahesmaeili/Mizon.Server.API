using Mizon.Server.Logic.Requests.Search;
using Mizon.Server.Logic.Responses.Search;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Linq.Expressions;

namespace Mizon.Server.Logic.Operations.Search;

public sealed class GlobalSearchOperation(DbContextOptions<MizonDatabaseContext> databaseOptions)
{
    private const int MaximumResultsPerType = 10;
    private static readonly Expression<Func<Client, GlobalSearchItemResponse>> ClientResultProjection = client => new()
    {
        ItemType = "Client",
        PublicId = client.PublicId,
        Username = client.ClientUsernames
            .Where(username => username.IsActive)
            .Select(username => username.Username)
            .FirstOrDefault(),
        FirstName = client.FirstName,
        LastName = client.LastName,
        NickName = client.NickName,
        ProfileMediaPublicId = client.ClientProfiles
            .Where(profile => profile.IsDefault)
            .Select(profile => (Guid?)profile.Media!.PublicId)
            .FirstOrDefault()
    };

    public async IAsyncEnumerable<GlobalSearchItemResponse> SearchAsync(
        GlobalSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = request.Query.Trim();
        if (query.Length < 2)
            yield break;

        var pattern = $"%{query}%";
        var clientResults = CreateResultsChannel();
        var musicResults = CreateResultsChannel();
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var clientProducer = CompleteWriterAsync(
            WriteClientResultsAsync(clientResults.Writer, query, pattern, producerCancellation.Token),
            clientResults.Writer);
        var musicProducer = CompleteWriterAsync(
            WriteMusicResultsAsync(musicResults.Writer, query, pattern, producerCancellation.Token),
            musicResults.Writer);

        try
        {
            await foreach (var result in MergeResultsAsync(
                clientResults.Reader,
                musicResults.Reader,
                producerCancellation.Token))
                yield return result;
        }
        finally
        {
            // A disconnected/finished consumer must not leave a producer waiting
            // for space in its bounded channel with an open database reader.
            await producerCancellation.CancelAsync();
            await Task.WhenAll(clientProducer, musicProducer);
        }
    }

    private static Channel<GlobalSearchItemResponse> CreateResultsChannel() =>
        Channel.CreateBounded<GlobalSearchItemResponse>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    private static async Task CompleteWriterAsync(
        Task producerTask,
        ChannelWriter<GlobalSearchItemResponse> writer)
    {
        try
        {
            await producerTask;
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private static async IAsyncEnumerable<GlobalSearchItemResponse> MergeResultsAsync(
        ChannelReader<GlobalSearchItemResponse> clientResults,
        ChannelReader<GlobalSearchItemResponse> musicResults,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<bool>? clientIsAvailableTask = clientResults.WaitToReadAsync(cancellationToken).AsTask();
        Task<bool>? musicIsAvailableTask = musicResults.WaitToReadAsync(cancellationToken).AsTask();
        var preferClient = true;

        while (clientIsAvailableTask is not null || musicIsAvailableTask is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Race every next result. Alternating only breaks ties between two
            // ready channels; an empty source never holds up the other source.
            bool readClient;
            if (clientIsAvailableTask is null) readClient = false;
            else if (musicIsAvailableTask is null) readClient = true;
            else
            {
                if (!clientIsAvailableTask.IsCompleted && !musicIsAvailableTask.IsCompleted)
                    await Task.WhenAny(clientIsAvailableTask, musicIsAvailableTask);

                // Completed ValueTask<bool> instances can share a cached Task;
                // select the source by readiness, never by Task identity.
                readClient = clientIsAvailableTask.IsCompleted;
                if (clientIsAvailableTask.IsCompleted && musicIsAvailableTask.IsCompleted)
                    readClient = preferClient;
            }
            var availableTask = readClient ? clientIsAvailableTask! : musicIsAvailableTask!;

            if (readClient)
            {
                if (await availableTask)
                {
                    yield return await clientResults.ReadAsync(cancellationToken);
                    clientIsAvailableTask = clientResults.WaitToReadAsync(cancellationToken).AsTask();
                    preferClient = false;
                }
                else clientIsAvailableTask = null;
            }
            else
            {
                if (await availableTask)
                {
                    yield return await musicResults.ReadAsync(cancellationToken);
                    musicIsAvailableTask = musicResults.WaitToReadAsync(cancellationToken).AsTask();
                    preferClient = true;
                }
                else musicIsAvailableTask = null;
            }
        }
    }

    private async Task WriteClientResultsAsync(
        ChannelWriter<GlobalSearchItemResponse> writer,
        string query,
        string pattern,
        CancellationToken cancellationToken)
    {
        await using var database = new MizonDatabaseContext(databaseOptions);

        var clients = database.Clients
            .AsNoTracking()
            .Where(client => client.ClientUsernames
                .Where(username => username.IsActive)
                .Any(username => EF.Functions.Like(username.Username, pattern)))
            .Select(ClientResultProjection)
            .Union(database.Clients
                .AsNoTracking()
                .Where(client => EF.Functions.Like(client.FirstName, pattern)
                    || EF.Functions.Like(client.LastName, pattern)
                    || (client.NickName != null && EF.Functions.Like(client.NickName, pattern)))
                .Select(ClientResultProjection))
            .OrderByDescending(item => item.Username == query)
            .ThenBy(item => item.Username)
            .Take(MaximumResultsPerType)
            .AsAsyncEnumerable();

        await foreach (var client in clients.WithCancellation(cancellationToken))
        {
            await writer.WriteAsync(client, cancellationToken);
            await Task.Yield();
        }
    }

    private async Task WriteMusicResultsAsync(
        ChannelWriter<GlobalSearchItemResponse> writer,
        string query,
        string pattern,
        CancellationToken cancellationToken)
    {
        await using var database = new MizonDatabaseContext(databaseOptions);

        var musics = database.Musics
            .AsNoTracking()
            .Where(music => music.IsActive)
            .Where(music => EF.Functions.Like(music.Title, pattern)
                || (music.PersianTitle != null && EF.Functions.Like(music.PersianTitle, pattern)))
            .Select(music => new GlobalSearchItemResponse
            {
                ItemType = "Music",
                PublicId = music.PublicId,
                Title = music.Title,
                PersianTitle = music.PersianTitle,
                CoverMediaPublicId = music.MusicCover == null
                    ? null
                    : music.MusicCover.Media!.PublicId
            })
            .OrderByDescending(item => item.Title == query)
            .ThenBy(item => item.Title)
            .Take(MaximumResultsPerType)
            .AsAsyncEnumerable();

        await foreach (var music in musics.WithCancellation(cancellationToken))
        {
            await writer.WriteAsync(music, cancellationToken);
            await Task.Yield();
        }
    }
}
