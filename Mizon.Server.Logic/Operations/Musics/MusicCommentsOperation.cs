namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicCommentsRequest, MusicCommentsResponse>
{
    public async Task<BaseApiResponse<MusicCommentsResponse>> HandleAsync(BaseApiRequest<MusicCommentsRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == request.Content!.MusicPublicId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
        }

        var comments = await mizonDatabaseContext.MusicComments
            .AsNoTracking()
            .Where(x => x.MusicId == music.Id)
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Id)
            .Select(x => new MusicCommentItemResponse
            {
                PublicId = x.PublicId,
                Text = x.Text,
                Timestamp = x.Timestamp,
                ClientPublicId = x.Client!.PublicId,
                FirstName = x.Client.FirstName,
                LastName = x.Client.LastName,
                Username = x.Client.ClientUsernames
                    .Where(username => username.IsActive)
                    .OrderByDescending(username => username.Id)
                    .Select(username => username.Username)
                    .FirstOrDefault(),
                ProfileMediaPublicId = x.Client!.ClientProfiles
                    .Where(profile => profile.IsDefault)
                    .Select(profile => profile.Media!.PublicId)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new() { Content = new() { MusicComments = comments } };
    }
}
