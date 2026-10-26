namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicLyricOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicLyricRequest, MusicLyricResponse>
{
    public async Task<BaseApiResponse<MusicLyricResponse>> HandleAsync(BaseApiRequest<MusicLyricRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
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


        var musicLyricLanguageResponses = await mizonDatabaseContext.MusicLyrics
           .AsNoTracking()
           .Where(m => m.MusicId == music.Id)
           .Select(m => new MusicLyricLanguageResponse
           {
               MusicLyricPublicId = m.PublicId,
               LanguagePublicId = m.Language!.PublicId,
               LanguageTitle = m.Language.Title,
               LanguagePersianTitle = m.Language.PersianTitle,
           })
           .ToListAsync(cancellationToken);


        return new()
        {
            Content = new()
            {
                MusicLyricLanguages = musicLyricLanguageResponses
            }
        };
    }
}