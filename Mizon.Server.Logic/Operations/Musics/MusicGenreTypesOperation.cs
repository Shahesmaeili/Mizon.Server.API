namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicGenreTypesOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicGenreTypesRequest, MusicGenreTypesResponse>
{
    public async Task<BaseApiResponse<MusicGenreTypesResponse>> HandleAsync(BaseApiRequest<MusicGenreTypesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var musicGenreTypeItemResponses = await mizonDatabaseContext.MusicGenreTypes
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Where(m => m.MusicGenres.Any())
            .Select(m => new MusicGenreTypeItemResponse
            {
                PublicId = m.PublicId,
                Title = m.Title,
                PersianTitle = m.PersianTitle,
                AccentColorHex = m.AccentColorHex,
                IconSymbol = m.IconSymbol,
                MusicsCount = m.MusicGenres.Count
            })
            .ToListAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                MusicGenreTypes = musicGenreTypeItemResponses
            }
        };
    }
}
