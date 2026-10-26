namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicLyricLineOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicLyricLineRequest, MusicLyricLineResponse>
{
    public async Task<BaseApiResponse<MusicLyricLineResponse>> HandleAsync(BaseApiRequest<MusicLyricLineRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var musicLyric = await mizonDatabaseContext.MusicLyrics
              .AsNoTracking()
              .Where(m => m.PublicId == request.Content!.MusicLyricPublicId)
              .Select(m => new
              {
                  m.Id,
              })
              .FirstOrDefaultAsync(cancellationToken);

        if (musicLyric is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicLyricNotFound
                }
            };
        }


        var musicLyricLineItemResponses = await mizonDatabaseContext.MusicLyricLines
           .AsNoTracking()
           .Where(m => m.MusicLyricId == musicLyric.Id)
           .OrderBy(m => m.LineOrder)
           .ThenBy(m => m.Id)
           .Select(m => new MusicLyricLineItemResponse
           {
               StartTimeInMilliseconds = m.StartTimeInMilliseconds,
               EndTimeInMilliseconds = m.EndTimeInMilliseconds,
               LineOrder = m.LineOrder,
               Text = m.Text,
               MusicLyricLineWords = m.MusicLyricLineWords
               .OrderBy(l => l.WordOrder)
               .ThenBy(l => l.Id)
               .Select(l => new MusicLyricLineWordResponse()
               {
                   EndTimeInMilliseconds = l.EndTimeInMilliseconds,
                   StartTimeInMilliseconds = l.StartTimeInMilliseconds,
                   WordOrder = l.WordOrder,
                   Text = l.Text,
               }).ToList()
           })
           .ToListAsync(cancellationToken);


        return new()
        {
            Content = new()
            {
                MusicLyricLines = musicLyricLineItemResponses
            }
        };
    }
}
