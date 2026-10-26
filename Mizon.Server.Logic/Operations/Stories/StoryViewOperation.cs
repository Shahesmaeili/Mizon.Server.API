namespace Mizon.Server.Logic.Operations.Stories;

public sealed class StoryViewOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<StoryRequest, StoryResponse>
{
    public async Task<BaseApiResponse<StoryResponse>> HandleAsync(BaseApiRequest<StoryRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var story = await mizonDatabaseContext.Stories
            .Where(x => x.PublicId == request.Content!.StoryPublicId)
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);
        if (story is null || requestMetadata!.ClientId is null)
            return new() { Error = new() { Code = 11000 } };

        var platformVersionId = await mizonDatabaseContext.ClientSessions.Where(x => x.PublicId == requestMetadata.SessionPublicId).Select(x => (short?)x.PlatformVersionId).FirstOrDefaultAsync(cancellationToken);
        if (platformVersionId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var alreadyViewed = await mizonDatabaseContext.StoryViews
            .Where(x => x.StoryId == story.Id)
            .Where(x => x.ClientId == requestMetadata.ClientId)
            .AnyAsync(cancellationToken);
        if (!alreadyViewed)
        {
            mizonDatabaseContext.StoryViews.Add(new() { StoryId = story.Id, ClientId = requestMetadata.ClientId.Value, PlatformVersionId = platformVersionId.Value });
            await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        }

        return new() { Content = new() { PublicId = story.PublicId, ViewedByMe = true } };
    }
}
