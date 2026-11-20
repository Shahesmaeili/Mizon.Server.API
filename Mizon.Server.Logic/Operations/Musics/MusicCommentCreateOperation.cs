namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentCreateOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicCommentCreateRequest, MusicCommentCreateResponse>
{
    public async Task<BaseApiResponse<MusicCommentCreateResponse>> HandleAsync(BaseApiRequest<MusicCommentCreateRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (request.Content is not { } input || input.Text?.Trim() is not { Length: > 0 and <= 2000 } text ||
            requestMetadata?.ClientId is not int clientId)
        {
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };
        }

        var music = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(x => x.PublicId == input.MusicPublicId)
            .Where(x => x.IsActive)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
        }

        if (input.Timestamp is TimeSpan position)
        {
            var duration = await mizonDatabaseContext.MusicFiles.AsNoTracking()
                .Where(file => file.MusicId == music.Id).MaxAsync(file => (TimeSpan?)file.Duration, cancellationToken);
            if (position < TimeSpan.Zero || position >= TimeSpan.FromDays(1) || duration is null || position > duration)
                return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };
        }

        int? parentId = null;
        if (input.ParentMusicCommentPublicId is Guid parent)
        {
            parentId = await mizonDatabaseContext.MusicComments.AsNoTracking().Where(x => x.MusicId == music.Id)
                .Where(x => x.PublicId == parent).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
            if (parentId is null)
                return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicCommentNotFound } };
        }
        var tags = await MusicCommentTags.ResolveAsync(mizonDatabaseContext, input.Tags, cancellationToken);
        if (tags is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };

        var platform = await mizonDatabaseContext.ClientSessions.AsNoTracking()
            .Where(session => session.PublicId == requestMetadata.SessionPublicId)
            .Where(session => session.ClientId == clientId)
            .Select(session => (short?)session.PlatformVersionId).SingleOrDefaultAsync(cancellationToken);

        var musicComment = new MusicComment
        {
            PublicId = Guid.NewGuid(),
            MusicId = music.Id,
            ClientId = clientId,
            PlatformVersionId = platform,
            ParentMusicCommentId = parentId,
            Text = text,
            Timestamp = input.Timestamp,
            Tags = tags
        };

        mizonDatabaseContext.MusicComments.Add(musicComment);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);

        return new() { Content = new() { MusicCommentPublicId = musicComment.PublicId } };
    }
}
