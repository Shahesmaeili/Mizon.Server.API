namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistCreateOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistCreateRequest, MusicPlaylistCreateResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistCreateResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistCreateRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var title = request.Content!.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 60)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicPlaylistTitle }
            };
        }

        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == request.Content.MusicPublicId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound }
            };
        }

        var playlist = new MusicPlaylist
        {
            ClientId = requestMetadata!.ClientId!.Value,
            Title = title
        };

        playlist.MusicPlaylistMusics.Add(new()
        {
            MusicId = music.Id
        });

        mizonDatabaseContext.MusicPlaylists.Add(playlist);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                MusicPlaylistPublicId = playlist.PublicId
            }
        };
    }
}
