namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicContributorMusicsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicContributorMusicsRequest, MusicTabMusicsResponse>
{
    public async Task<BaseApiResponse<MusicTabMusicsResponse>> HandleAsync(BaseApiRequest<MusicContributorMusicsRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(m => m.PublicId == request.Content!.MusicPublicId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var personIds = await mizonDatabaseContext.MusicContributors
            .AsNoTracking()
            .Where(mc => mc.MusicId == music.Id)
            .Select(mc => mc.PersonId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (personIds.Count == 0)
            return new() { Content = new() };

        var musicIds = await mizonDatabaseContext.MusicContributors
            .AsNoTracking()
            .Where(mc => personIds.Contains(mc.PersonId))
            .Where(mc => mc.MusicId != music.Id)
            .Select(mc => mc.MusicId)
            .Distinct()
            .Join(mizonDatabaseContext.Musics.AsNoTracking(), id => id, candidate => candidate.Id, (id, candidate) => candidate)
            .Where(candidate => candidate.IsActive)
            .OrderByDescending(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .Take(30)
            .ToListAsync(cancellationToken);

        return await MusicTabMusicResponseFactory.CreateAsync(mizonDatabaseContext, musicIds, cancellationToken);
    }
}
