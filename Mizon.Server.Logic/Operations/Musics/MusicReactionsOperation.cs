namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicReactionsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicReactionsRequest, MusicReactionsResponse>
{
    public async Task<BaseApiResponse<MusicReactionsResponse>> HandleAsync(BaseApiRequest<MusicReactionsRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
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


        var musicReactionItemResponse = await mizonDatabaseContext.MusicReactions
           .AsNoTracking()
           .Where(m => m.MusicId == music.Id)
           .Where(m => m.MusicReactionType!.IsActive)
           .GroupBy(r => new
           {
               r.MusicReactionTypeId,
               r.MusicReactionType!.Emoji,
               r.MusicReactionType!.PublicId
           })
           .Select(g => new MusicReactionItemResponse
           {
               Emoji = g.Key.Emoji,
               MusicReactionTypePublicId = g.Key.PublicId,
               Count = g.Count(),
               ReactedByMe = g.Any(r => r.ClientId == requestMetadata!.ClientId)
           })
          .ToListAsync(cancellationToken);


        return new()
        {
            Content = new()
            {
                MusicReactionItems = musicReactionItemResponse
            }
        };
    }
}