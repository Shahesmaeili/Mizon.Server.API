using Microsoft.Data.SqlClient;

namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlayOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlayRequest, MusicPlayResponse>
{
    public async Task<BaseApiResponse<MusicPlayResponse>> HandleAsync(
        BaseApiRequest<MusicPlayRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId || requestMetadata.SessionPublicId is not Guid sessionPublicId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        if (request.Content is not { } content || content.MusicPublicId == Guid.Empty || content.PlaybackPublicId == Guid.Empty)
            return InvalidPlayback();

        var platformVersionId = await mizonDatabaseContext.ClientSessions.AsNoTracking()
            .Where(x => x.PublicId == sessionPublicId)
            .Where(x => x.ClientId == clientId)
            .Where(x => x.LogoutDateTime == null)
            .Select(x => (short?)x.PlatformVersionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (platformVersionId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var musicId = await mizonDatabaseContext.Musics.AsNoTracking()
            .Where(x => x.PublicId == content.MusicPublicId)
            .Where(x => x.IsActive)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (musicId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var existingMusicId = await FindPlaybackMusicIdAsync(clientId, content.PlaybackPublicId, cancellationToken);
        if (existingMusicId is not null)
            return existingMusicId == musicId ? new() { Content = new() } : InvalidPlayback();

        var play = new MusicPlay
        {
            ClientId = clientId,
            MusicId = musicId.Value,
            PlatformVersionId = platformVersionId.Value,
            PlaybackPublicId = content.PlaybackPublicId
        };
        mizonDatabaseContext.MusicPlays.Add(play);

        try
        {
            await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A retried request can arrive before the first request finishes committing.
            mizonDatabaseContext.Entry(play).State = EntityState.Detached;
            existingMusicId = await FindPlaybackMusicIdAsync(clientId, content.PlaybackPublicId, cancellationToken);
            if (existingMusicId is null)
                throw;
            if (existingMusicId != musicId)
                return InvalidPlayback();
        }

        return new() { Content = new() };
    }

    private Task<int?> FindPlaybackMusicIdAsync(int clientId, Guid playbackPublicId, CancellationToken cancellationToken) =>
        mizonDatabaseContext.MusicPlays.AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .Where(x => x.PlaybackPublicId == playbackPublicId)
            .Select(x => (int?)x.MusicId)
            .FirstOrDefaultAsync(cancellationToken);

    private static BaseApiResponse<MusicPlayResponse> InvalidPlayback() =>
        new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicPlayRequest } };
}
