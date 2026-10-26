namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistMembershipOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistMembershipRequest, MusicPlaylistMembershipResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistMembershipResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistMembershipRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == request.Content!.MusicPublicId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound }
            };
        }

        var selectedPlaylistPublicIds = request.Content.MusicPlaylistPublicIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        var ownedPlaylists = await mizonDatabaseContext.MusicPlaylists
            .Where(x => x.ClientId == requestMetadata!.ClientId)
            .Select(x => new { x.Id, x.PublicId })
            .ToListAsync(cancellationToken);

        var selectedPlaylistIds = ownedPlaylists
            .Where(x => selectedPlaylistPublicIds.Contains(x.PublicId))
            .Select(x => x.Id)
            .ToHashSet();

        if (selectedPlaylistIds.Count != selectedPlaylistPublicIds.Length)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Musics.MusicPlaylistNotFound }
            };
        }

        var ownedPlaylistIds = ownedPlaylists.Select(x => x.Id).ToArray();
        await mizonDatabaseContext.MusicPlaylistMusics
            .Where(x => x.MusicId == music.Id)
            .Where(x => ownedPlaylistIds.Contains(x.MusicPlaylistId))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var playlistId in selectedPlaylistIds)
        {
            mizonDatabaseContext.MusicPlaylistMusics.Add(new()
            {
                MusicPlaylistId = playlistId,
                MusicId = music.Id
            });
        }

        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);

        return new() { Content = new() };
    }
}
