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

        if (musicFiles.Count == 0)
            return new() { Content = new() { IsAvailable = false } };

        var musicFileIds = musicFiles.Select(item => item.Id).ToArray();
        var stems = await mizonDatabaseContext.MusicFileStems
            .AsNoTracking()
            .Where(item => musicFileIds.Contains(item.MusicFileId))
            .Where(item => item.MusicFileStemType!.Title == "Vocals" || item.MusicFileStemType.Title == "Instrumental")
            .OrderBy(item => item.MusicFileId)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.MusicFileId,
                StemType = item.MusicFileStemType!.Title,
                MediaPublicId = item.Media!.PublicId
            })
            .ToListAsync(cancellationToken);

        // Keep the file and stem reads separate, but batch the stem lookup.
        // Both stems must belong to the same original file.
        var stemsByFile = stems.ToLookup(item => item.MusicFileId);
        foreach (var musicFile in musicFiles)
        {
            var musicFileStems = stemsByFile[musicFile.Id];
            var vocalsMediaPublicId = musicFileStems
                .Where(item => item.StemType == "Vocals")
                .Select(item => (Guid?)item.MediaPublicId)
                .FirstOrDefault();
            var instrumentalMediaPublicId = musicFileStems
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
