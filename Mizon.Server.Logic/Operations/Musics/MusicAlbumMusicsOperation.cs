namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicAlbumMusicsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicAlbumMusicsRequest, MusicTabMusicsResponse>
{
    public async Task<BaseApiResponse<MusicTabMusicsResponse>> HandleAsync(BaseApiRequest<MusicAlbumMusicsRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(m => m.PublicId == request.Content!.MusicPublicId)
            .Select(m => new { m.MusicAlbumId })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        if (music.MusicAlbumId is null)
            return new() { Content = new() };

        var musicIds = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(m => m.MusicAlbumId == music.MusicAlbumId)
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .Take(30)
            .ToListAsync(cancellationToken);

        return await MusicTabMusicResponseFactory.CreateAsync(mizonDatabaseContext, musicIds, cancellationToken);
    }
}
