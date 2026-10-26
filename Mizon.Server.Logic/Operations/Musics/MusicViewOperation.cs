namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicViewOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicViewRequest, MusicViewResponse>
{
    public async Task<BaseApiResponse<MusicViewResponse>> HandleAsync(BaseApiRequest<MusicViewRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
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

        var clientSession = await mizonDatabaseContext.ClientSessions
           .AsNoTracking()
           .Where(m => m.PublicId == requestMetadata!.SessionPublicId)
           .Select(m => new
           {
               m.PlatformVersionId,
           })
           .FirstOrDefaultAsync(cancellationToken);

        if (clientSession is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Clients.ClientSessionNotFound
                }
            };
        }

        var existing = await mizonDatabaseContext.MusicViews
            .AsNoTracking()
            .Where(m => m.MusicId == music.Id)
            .Where(m => m.ClientId == requestMetadata!.ClientId)
            .Where(m => m.PlatformVersionId == clientSession.PlatformVersionId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            mizonDatabaseContext.MusicViews.Add(new()
            {
                ClientId = requestMetadata!.ClientId!.Value,
                MusicId = music.Id,
                PlatformVersionId = clientSession.PlatformVersionId,
            });

            await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        }

        return new()
        {
            Content = new()
        };
    }
}
