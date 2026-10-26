namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicReactionOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicReactionRequest, MusicReactionResponse>
{
    public async Task<BaseApiResponse<MusicReactionResponse>> HandleAsync(BaseApiRequest<MusicReactionRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
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


        var musicReactionType = await mizonDatabaseContext.MusicReactionTypes
          .AsNoTracking()
          .Where(m => m.PublicId == request.Content!.MusicReactionTypePublicId)
          .Select(m => new
          {
              m.Id,
          })
          .FirstOrDefaultAsync(cancellationToken);

        if (musicReactionType is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicReactionTypeNotFound
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



        var musicReaction = await mizonDatabaseContext.MusicReactions
               .AsNoTracking()
               .Where(m => m.MusicId == music.Id)
               .Where(m => m.MusicReactionTypeId == musicReactionType.Id)
               .Where(m => m.ClientId == requestMetadata!.ClientId)
               .Select(m => new
               {
                   m.Id
               })
               .FirstOrDefaultAsync(cancellationToken);

        if (request.Content!.Reacted)
        {
            if (musicReaction is null)
            {
                mizonDatabaseContext.MusicReactions.Add(new()
                {
                    ClientId = requestMetadata!.ClientId!.Value,
                    MusicId = music.Id,
                    MusicReactionTypeId = musicReactionType.Id,
                    PlatformVersionId = clientSession.PlatformVersionId,
                });

                await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (musicReaction is not null)
            {
                await mizonDatabaseContext.MusicReactions
                    .Where(x => x.Id == musicReaction.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        return new()
        {
            Content = new()
        };
    }
}