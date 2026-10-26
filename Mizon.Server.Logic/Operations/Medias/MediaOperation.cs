using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;

namespace Mizon.Server.Logic.Operations.Medias;

public sealed class MediaOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MediaRequest, MediaResponse>
{
    public async Task<BaseApiResponse<MediaResponse>> HandleAsync(BaseApiRequest<MediaRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var media = await mizonDatabaseContext.Medias
            .AsNoTracking()
            .Where(m => m.PublicId == request.Content!.MediaPublicId)
            .Select(m => new
            {
                m.PublicId,
                m.TotalChunks,
                m.ChunkSize,
                m.FileHash,
                m.FileName,
                m.FileSize,
                m.StoragePath,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Medias.MediaNotFound
                }
            };
        }

        return new()
        {
            Content = new()
            {
                MediaPublicId = media.PublicId,
                TotalChunks = media.TotalChunks,
                ChunkSize = media.ChunkSize,
                FileHash = media.FileHash,
                FileName = media.FileName,
                FileSize = media.FileSize,
                StoragePath = media.StoragePath,
            }
        };
    }
}
