namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistsRequest, MusicPlaylistsResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistsResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistsRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == request.Content!.MusicPublicId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
            return MusicNotFound();

        var playlistItems = await mizonDatabaseContext.MusicPlaylists
            .AsNoTracking()
            .Where(x => x.ClientId == requestMetadata!.ClientId)
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.PublicId,
                x.Title,
                MusicsCount = x.MusicPlaylistMusics.Count,
                ContainsCurrentMusic = x.MusicPlaylistMusics
                    .Where(item => item.MusicId == music.Id)
                    .Any(),
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
                MusicPlaylists = playlistItems.Select(x => new MusicPlaylistListItemResponse
                {
                    PublicId = x.PublicId,
                    Title = x.Title,
                    MusicsCount = x.MusicsCount,
                    LastMusicCoverMediaPublicId = x.LastMusicCoverMediaPublicId,
                    ContainsCurrentMusic = x.ContainsCurrentMusic
                }).ToList()
            }
        };
    }

    internal static BaseApiResponse<MusicPlaylistsResponse> MusicNotFound() => new()
    {
        Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound }
    };
}
