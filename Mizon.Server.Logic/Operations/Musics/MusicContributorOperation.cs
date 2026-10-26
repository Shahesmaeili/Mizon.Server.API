namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicContributorOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicContributorRequest, MusicContributorsResponse>
{
    public async Task<BaseApiResponse<MusicContributorsResponse>> HandleAsync(BaseApiRequest<MusicContributorRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
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

        var contributors = await mizonDatabaseContext.MusicContributors
           .AsNoTracking()
           .Where(mc => mc.MusicId == music.Id)
           .Select(mc => new MusicContributorItemResponse
           {
               PersonPublicId = mc.Person!.PublicId,
               FirstName = mc.Person.FirstName,
               LastName = mc.Person.LastName,
               ProfileMediaPublicId = mc.Person.ProfileMedia == null ? null : mc.Person.ProfileMedia.PublicId,
               MusicContributorTypePublicId = mc.MusicContributorType!.PublicId,
               MusicContributorTypeTitle = mc.MusicContributorType.Title,
               MusicContributorTypePersianTitle = mc.MusicContributorType.PersianTitle
           })
           .ToListAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                Contributors = contributors
            }
        };
    }
}
