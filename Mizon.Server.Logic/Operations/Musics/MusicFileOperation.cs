namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicFileOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicFileRequest, MusicFileResponse>
{
    public async Task<BaseApiResponse<MusicFileResponse>> HandleAsync(BaseApiRequest<MusicFileRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
              .AsNoTracking()
              .Where(m => m.PublicId == request.Content!.MusicPublicId)
              .Select(m => new
              {
                  m.Id,
              })
              .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicNotFound
                }
            };
        }

        
        var musicFile = await mizonDatabaseContext.MusicFiles
           .AsNoTracking()
           .Where(m => m.MusicId == music.Id)
           .Select(m => new
           {
               MusicFilePublicId = m.PublicId,
               MediaPublicId = m.Media!.PublicId,
               TotalChunks = m.Media!.TotalChunks,
               ChunkSize = m.Media!.ChunkSize,
               FileHash = m.Media!.FileHash,
               FileName = m.Media!.FileName,
               FileSize = m.Media!.FileSize,
               StoragePath = m.Media!.StoragePath,
               m.Duration,
           })
           .FirstOrDefaultAsync(cancellationToken);

        if (musicFile is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicFileNotFound
                }
            };
        }

        return new()
        {
            Content = new()
            {
                MusicFilePublicId = musicFile.MusicFilePublicId,
                MediaPublicId = musicFile.MediaPublicId,
                TotalChunks = musicFile.TotalChunks,
                ChunkSize = musicFile.ChunkSize,
                FileHash = musicFile.FileHash,
                FileName = musicFile.FileName,
                FileSize = musicFile.FileSize,
                StoragePath = musicFile.StoragePath,
                Duration = musicFile.Duration,
            }
        };
    }
}
