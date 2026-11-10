namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistsRequest, MusicPlaylistsResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistsResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistsRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound }
            };
        }

        int? musicId = null;
        if (request.Content?.MusicPublicId is Guid musicPublicId)
        {
            musicId = await mizonDatabaseContext.Musics
                .AsNoTracking()
                .Where(x => x.PublicId == musicPublicId)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (musicId is null)
                return MusicNotFound();
        }

        var playlistItems = await mizonDatabaseContext.MusicPlaylists
            .AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.Id)
            .Select(x => new MusicPlaylistListItemResponse
            {
                PublicId = x.PublicId,
                Title = x.Title,
                MusicsCount = x.MusicPlaylistMusics.Count,
                ContainsCurrentMusic = musicId.HasValue
                    ? x.MusicPlaylistMusics.Where(item => item.MusicId == musicId.Value).Any()
                    : false,
                LastMusicCoverMediaPublicId = x.MusicPlaylistMusics
                    .OrderByDescending(item => item.Id)
                    .Select(item => item.Music!.MusicCover == null
                        ? (Guid?)null
                        : item.Music.MusicCover.Media!.PublicId)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                MusicPlaylists = playlistItems
            }
        };
    }

    internal static BaseApiResponse<MusicPlaylistsResponse> MusicNotFound() => new()
    {
        Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound }
    };
}
