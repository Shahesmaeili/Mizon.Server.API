namespace Mizon.Server.Logic.Operations.Stories;

public sealed class StoriesOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<StoriesRequest, StoriesResponse>
{
    public async Task<BaseApiResponse<StoriesResponse>> HandleAsync(BaseApiRequest<StoriesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var storiesQuery = mizonDatabaseContext.Stories.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > now);

        if (request.Content?.StoryPublicId is Guid storyPublicId)
            storiesQuery = storiesQuery.Where(x => x.PublicId == storyPublicId);

        if (request.Content?.MusicPublicId is Guid musicPublicId)
        {
            storiesQuery = storiesQuery.Where(x => x.StoryWidgets
                .Where(widget => widget.MusicWidget != null)
                .Any(widget => widget.MusicWidget!.Music!.PublicId == musicPublicId));
        }

        var stories = await storiesQuery.OrderByDescending(x => x.CreateDateTime)
            .Select(x => new
            {
                x.Id,
                x.PublicId,
                x.Title,
                x.Client!.FirstName,
                x.Client.LastName,
                CreatorProfileMediaPublicId = x.Client.ClientProfiles
                    .Where(profile => profile.IsDefault)
                    .Select(profile => (Guid?)profile.Media!.PublicId)
                    .FirstOrDefault(),
                PreviewMediaPublicId = x.PreviewMedia == null
                    ? x.ContentMedia!.PublicId
                    : x.PreviewMedia.PublicId,
            })
            .ToListAsync(cancellationToken);

        var storyIds = stories.Select(x => x.Id).ToArray();
        var viewedByMe = requestMetadata?.ClientId is not int clientId
            ? []
            : await mizonDatabaseContext.StoryViews.AsNoTracking()
                .Where(x => storyIds.Contains(x.StoryId))
                .Where(x => x.ClientId == clientId)
                .Select(x => x.StoryId)
                .ToHashSetAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                Stories = stories.Select(story => new StoryListItemResponse
                {
                    PublicId = story.PublicId,
                    CreatorFirstName = story.FirstName,
                    CreatorLastName = story.LastName,
                    CreatorProfileMediaPublicId = story.CreatorProfileMediaPublicId,
                    PreviewMediaPublicId = story.PreviewMediaPublicId,
                    Title = story.Title,
                    ViewedByMe = viewedByMe.Contains(story.Id),
                }).ToList(),
            },
        };
    }
}
