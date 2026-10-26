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

        _ = CompleteWriterAsync(
            WriteClientResultsAsync(clientResults.Writer, query, pattern, cancellationToken),
            clientResults.Writer);
        _ = CompleteWriterAsync(
            WriteMusicResultsAsync(musicResults.Writer, query, pattern, cancellationToken),
            musicResults.Writer);

        await foreach (var result in MergeResultsAsync(
            clientResults.Reader,
            musicResults.Reader,
            cancellationToken))
            yield return result;
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
        var clientIsAvailableTask = clientResults.WaitToReadAsync(cancellationToken).AsTask();
        var musicIsAvailableTask = musicResults.WaitToReadAsync(cancellationToken).AsTask();
        var firstAvailableTask = await Task.WhenAny(clientIsAvailableTask, musicIsAvailableTask);

        var readClientNext = firstAvailableTask == clientIsAvailableTask
            ? await clientIsAvailableTask
            : !await musicIsAvailableTask;

        var clientIsOpen = true;
        var musicIsOpen = true;

        while (clientIsOpen || musicIsOpen)
        {
            if (readClientNext && clientIsOpen)
            {
                if (await clientResults.WaitToReadAsync(cancellationToken))
                {
                    yield return await clientResults.ReadAsync(cancellationToken);
                    readClientNext = false;
                    continue;
                }

                clientIsOpen = false;
            }

            if (!readClientNext && musicIsOpen)
            {
                if (await musicResults.WaitToReadAsync(cancellationToken))
                {
                    yield return await musicResults.ReadAsync(cancellationToken);
                    readClientNext = true;
                    continue;
                }

                musicIsOpen = false;
            }

            readClientNext = clientIsOpen;
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
