namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentCreateOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicCommentCreateRequest, MusicCommentCreateResponse>
{
    public async Task<BaseApiResponse<MusicCommentCreateResponse>> HandleAsync(BaseApiRequest<MusicCommentCreateRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content!.Text))
        {
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
        }

        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == request.Content.MusicPublicId)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
        }

        var musicComment = new MusicComment
        {
            PublicId = Guid.NewGuid(),
            MusicId = music.Id,
            ClientId = requestMetadata!.ClientId!.Value,
            Text = request.Content.Text.Trim(),
            Timestamp = request.Content.Timestamp
        };

        mizonDatabaseContext.MusicComments.Add(musicComment);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);

        return new() { Content = new() { MusicCommentPublicId = musicComment.PublicId } };
    }
}
