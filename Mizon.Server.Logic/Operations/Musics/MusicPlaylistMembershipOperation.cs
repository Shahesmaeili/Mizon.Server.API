namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistMembershipOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistMembershipRequest, MusicPlaylistMembershipResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistMembershipResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistMembershipRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        if (request.Content is not { MusicPlaylistPublicIds: not null } content)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicPlaylistNotFound } };

        var musicId = await mizonDatabaseContext.Musics.AsNoTracking()
            .Where(x => x.PublicId == content.MusicPublicId)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (musicId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var selectedPublicIds = content.MusicPlaylistPublicIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        await using var transaction = await mizonDatabaseContext.Database.BeginTransactionAsync(cancellationToken);
        await MusicPlaylistWriteLock.AcquireAsync(mizonDatabaseContext, clientId, cancellationToken);

        var selectedIds = await mizonDatabaseContext.MusicPlaylists.AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .Where(x => selectedPublicIds.Contains(x.PublicId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (selectedIds.Count != selectedPublicIds.Length)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicPlaylistNotFound } };

        var existingIds = await mizonDatabaseContext.MusicPlaylistMusics.AsNoTracking()
            .Where(x => x.MusicId == musicId.Value)
            .Where(x => x.MusicPlaylist!.ClientId == clientId)
            .Select(x => x.MusicPlaylistId)
            .ToListAsync(cancellationToken);
        var addedIds = selectedIds.Except(existingIds).ToArray();
        var removedIds = existingIds.Except(selectedIds).ToArray();
        var changedIds = addedIds.Concat(removedIds).ToArray();

        // Repeated checkbox snapshots must not change membership IDs, the cover
        // or the library's recently-updated ordering.
        if (changedIds.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new() { Content = new() };
        }

        if (removedIds.Length > 0)
        {
            await mizonDatabaseContext.MusicPlaylistMusics
                .Where(x => x.MusicId == musicId.Value)
                .Where(x => removedIds.Contains(x.MusicPlaylistId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var playlistId in addedIds)
            mizonDatabaseContext.MusicPlaylistMusics.Add(new() { MusicPlaylistId = playlistId, MusicId = musicId.Value });

        var now = DateTimeOffset.UtcNow;
        await mizonDatabaseContext.MusicPlaylists
            .Where(x => x.ClientId == clientId)
            .Where(x => changedIds.Contains(x.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdateDateTime, now), cancellationToken);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new() { Content = new() };
    }
}
