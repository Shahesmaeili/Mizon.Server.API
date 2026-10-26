namespace Mizon.Server.Logic.Operations.Stories;

public sealed class StoryLikeOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<StoryLikeRequest, StoryResponse>
{
    public async Task<BaseApiResponse<StoryResponse>> HandleAsync(BaseApiRequest<StoryLikeRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata!.ClientId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var story = await mizonDatabaseContext.Stories
            .Where(x => x.PublicId == request.Content!.StoryPublicId)
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > DateTimeOffset.UtcNow)
            .Select(x => new { x.Id, x.PublicId })
            .FirstOrDefaultAsync(cancellationToken);
        if (story is null) return new() { Error = new() { Code = 11000 } };

        var like = await mizonDatabaseContext.StoryLikes
            .Where(x => x.StoryId == story.Id)
            .Where(x => x.ClientId == requestMetadata.ClientId)
            .FirstOrDefaultAsync(cancellationToken);
        if (request.Content.Liked && like is null)
        {
            var platformVersionId = await mizonDatabaseContext.ClientSessions.Where(x => x.PublicId == requestMetadata.SessionPublicId).Select(x => (short?)x.PlatformVersionId).FirstOrDefaultAsync(cancellationToken);
            if (platformVersionId is null) return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
            mizonDatabaseContext.StoryLikes.Add(new() { StoryId = story.Id, ClientId = requestMetadata.ClientId.Value, PlatformVersionId = platformVersionId.Value });
        }
        else if (!request.Content.Liked && like is not null)
            mizonDatabaseContext.StoryLikes.Remove(like);

        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        var likesCount = await mizonDatabaseContext.StoryLikes.CountAsync(x => x.StoryId == story.Id, cancellationToken);
        return new() { Content = new() { PublicId = story.PublicId, LikedByMe = request.Content.Liked, LikesCount = likesCount } };
    }
}
