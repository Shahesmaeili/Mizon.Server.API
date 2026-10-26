namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicStemMixOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicStemMixRequest, MusicStemMixResponse>
{
    public async Task<BaseApiResponse<MusicStemMixResponse>> HandleAsync(
        BaseApiRequest<MusicStemMixRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var musicId = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(item => item.PublicId == request.Content!.MusicPublicId)
            .Select(item => (int?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (musicId is null)
        {
            return new()
            {
                Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound }
            };
        }

        var musicFiles = await mizonDatabaseContext.MusicFiles
            .AsNoTracking()
            .Where(item => item.MusicId == musicId.Value)
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.PublicId })
            .ToListAsync(cancellationToken);

        foreach (var musicFile in musicFiles)
        {
            var stems = await mizonDatabaseContext.MusicFileStems
                .AsNoTracking()
                .Where(item => item.MusicFileId == musicFile.Id)
                .Where(item => item.MusicFileStemType!.Title == "Vocals" || item.MusicFileStemType.Title == "Instrumental")
                .Select(item => new
                {
                    StemType = item.MusicFileStemType!.Title,
                    MediaPublicId = item.Media!.PublicId
                })
                .ToListAsync(cancellationToken);

            var vocalsMediaPublicId = stems
                .Where(item => item.StemType == "Vocals")
                .Select(item => (Guid?)item.MediaPublicId)
                .FirstOrDefault();
            var instrumentalMediaPublicId = stems
                .Where(item => item.StemType == "Instrumental")
                .Select(item => (Guid?)item.MediaPublicId)
                .FirstOrDefault();

            if (vocalsMediaPublicId is not null && instrumentalMediaPublicId is not null)
            {
                return new()
                {
                    Content = new()
                    {
                        IsAvailable = true,
                        MusicFilePublicId = musicFile.PublicId,
                        VocalsMediaPublicId = vocalsMediaPublicId,
                        InstrumentalMediaPublicId = instrumentalMediaPublicId
                    }
                };
            }
        }

        return new() { Content = new() { IsAvailable = false } };
    }
}
