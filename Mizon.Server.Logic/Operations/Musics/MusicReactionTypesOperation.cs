namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicReactionTypesOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicReactionTypesRequest, MusicReactionTypesResponse>
{
    public async Task<BaseApiResponse<MusicReactionTypesResponse>> HandleAsync(BaseApiRequest<MusicReactionTypesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var musicReactionTypeItemResponses = await mizonDatabaseContext.MusicReactionTypes
              .AsNoTracking()
              .Where(m => m.IsActive)
              .Select(m => new MusicReactionTypeItemResponse
              {
                 Emoji = m.Emoji,
                 PublicId = m.PublicId,
              })
              .ToListAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                MusicReactionTypes = musicReactionTypeItemResponses
            }
        };
    }
}
