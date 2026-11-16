using Mizon.Server.Logic.Requests.Persons;
using Mizon.Server.Logic.Responses.Persons;

namespace Mizon.Server.Logic.Operations.Persons;

public sealed class PersonMusicsOperation(MizonDatabaseContext database)
    : IApiOperation<PersonMusicsRequest, PersonMusicsResponse>
{
    private const int PageSize = 24;

    public async Task<BaseApiResponse<PersonMusicsResponse>> HandleAsync(BaseApiRequest<PersonMusicsRequest> request,
        RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        if (request.Content is not { PersonPublicId: var publicId } input || publicId == Guid.Empty)
            return new() { Error = new() { Code = OperationErrorCodes.Persons.InvalidRequest } };

        var personId = await database.Persons.AsNoTracking().Where(x => x.PublicId == publicId)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (personId is null) return new() { Error = new() { Code = OperationErrorCodes.Persons.NotFound } };

        var query = PersonProfileQueries.MusicIds(database, personId.Value);
        if (input.BeforeMusicPublicId is Guid before)
        {
            // A removed/deactivated credit may still be a valid boundary for a
            // previously loaded page. A cursor belonging to another person is not.
            var cursor = await database.MusicContributors.AsNoTracking().Where(x => x.PersonId == personId.Value)
                .Where(x => x.Music!.PublicId == before).Select(x => (int?)x.MusicId).FirstOrDefaultAsync(cancellationToken);
            if (cursor is null) return new() { Error = new() { Code = OperationErrorCodes.Persons.InvalidCursor } };
            query = query.Where(x => x < cursor.Value);
        }

        var ids = await query.OrderByDescending(x => x).Take(PageSize + 1).ToListAsync(cancellationToken);
        var hasMore = ids.Count > PageSize;
        if (hasMore) ids.RemoveAt(PageSize);
        var page = await MusicTabMusicResponseFactory.CreateAsync(database, ids, cancellationToken, activeOnly: true);
        var items = page.Content!.Musics;
        return new()
        {
            Content = new()
            {
                Musics = items,
                NextBeforeMusicPublicId = hasMore && items.Count != 0 ? items[^1].PublicId : null
            }
        };
    }
}
