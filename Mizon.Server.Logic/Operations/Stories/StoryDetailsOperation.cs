namespace Mizon.Server.Logic.Operations.Stories;

public sealed class StoryDetailsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<StoryRequest, StoryResponse>
{
    public async Task<BaseApiResponse<StoryResponse>> HandleAsync(BaseApiRequest<StoryRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var story = await mizonDatabaseContext.Stories.AsNoTracking()
            .Where(x => x.PublicId == request.Content!.StoryPublicId)
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > now)
            .Select(x => new
            {
                x.Id, x.PublicId, x.Title, x.ExpireDateTime,
                CreatorClientPublicId = x.Client!.PublicId,
                x.Client.FirstName, x.Client.LastName,
                CreatorUsername = x.Client.ClientUsernames.Where(username => username.IsActive).Select(username => username.Username).FirstOrDefault(),
                CreatorProfileMediaPublicId = x.Client.ClientProfiles.Where(profile => profile.IsDefault).Select(profile => (Guid?)profile.Media!.PublicId).FirstOrDefault(),
                StoryContentTypePublicId = x.StoryContentType!.PublicId,
                StoryContentTypeTitle = x.StoryContentType.Title,
                StoryContentTypePersianTitle = x.StoryContentType.PersianTitle,
                ContentMediaPublicId = x.ContentMedia!.PublicId,
                PreviewMediaPublicId = x.PreviewMedia == null ? (Guid?)null : x.PreviewMedia.PublicId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (story is null)
            return new() { Error = new() { Code = 11000 } };

        var viewsCount = await mizonDatabaseContext.StoryViews.AsNoTracking().Where(x => x.StoryId == story.Id).CountAsync(cancellationToken);
        var likesCount = await mizonDatabaseContext.StoryLikes.AsNoTracking().Where(x => x.StoryId == story.Id).CountAsync(cancellationToken);
        var viewedByMe = requestMetadata?.ClientId is int currentClientId && await mizonDatabaseContext.StoryViews.AsNoTracking()
            .Where(x => x.StoryId == story.Id).Where(x => x.ClientId == currentClientId).AnyAsync(cancellationToken);
        var likedByMe = requestMetadata?.ClientId is int likingClientId && await mizonDatabaseContext.StoryLikes.AsNoTracking()
            .Where(x => x.StoryId == story.Id).Where(x => x.ClientId == likingClientId).AnyAsync(cancellationToken);

        var widgetRows = await mizonDatabaseContext.StoryWidgets.AsNoTracking()
            .Where(x => x.StoryId == story.Id)
            .Select(x => new { x.Id, x.PublicId, TypePublicId = x.StoryWidgetType!.PublicId, StylePublicId = x.StoryWidgetStyle!.PublicId, x.X, x.Y, x.Width, x.Height, x.Rotation, x.ZIndex })
            .ToListAsync(cancellationToken);
        var widgetIds = widgetRows.Select(x => x.Id).ToArray();
        var musicWidgets = await mizonDatabaseContext.StoryMusicWidgets.AsNoTracking()
            .Where(x => widgetIds.Contains(x.StoryWidgetId))
            .Select(x => new { x.StoryWidgetId, MusicPublicId = x.Music!.PublicId, x.Music.Title, CoverMediaPublicId = x.Music.MusicCover == null ? (Guid?)null : x.Music.MusicCover.Media!.PublicId })
            .ToDictionaryAsync(x => x.StoryWidgetId, cancellationToken);
        var clientWidgets = await mizonDatabaseContext.StoryClientWidgets.AsNoTracking()
            .Where(x => widgetIds.Contains(x.StoryWidgetId))
            .Select(x => new
            {
                x.StoryWidgetId,
                ClientPublicId = x.Client!.PublicId,
                x.Client.FirstName,
                x.Client.LastName,
                Username = x.Client.ClientUsernames.Where(username => username.IsActive).Select(username => username.Username).FirstOrDefault(),
                ProfileMediaPublicId = x.Client.ClientProfiles.Where(profile => profile.IsDefault).Select(profile => (Guid?)profile.Media!.PublicId).FirstOrDefault(),
            })
            .ToDictionaryAsync(x => x.StoryWidgetId, cancellationToken);
        var widgets = widgetRows.OrderBy(x => x.ZIndex).Select(widget => new StoryWidgetResponse
        {
            PublicId = widget.PublicId,
            StoryWidgetTypePublicId = widget.TypePublicId,
            StoryWidgetStylePublicId = widget.StylePublicId,
            X = widget.X, Y = widget.Y, Width = widget.Width, Height = widget.Height, Rotation = widget.Rotation, ZIndex = widget.ZIndex,
            Music = musicWidgets.TryGetValue(widget.Id, out var music) ? new() { MusicPublicId = music.MusicPublicId, Title = music.Title, CoverMediaPublicId = music.CoverMediaPublicId } : null,
            Client = clientWidgets.TryGetValue(widget.Id, out var client) ? new() { ClientPublicId = client.ClientPublicId, FirstName = client.FirstName, LastName = client.LastName, Username = client.Username, ProfileMediaPublicId = client.ProfileMediaPublicId } : null,
        }).ToList();

        return new() { Content = new()
        {
            PublicId = story.PublicId, CreatorClientPublicId = story.CreatorClientPublicId, CreatorFirstName = story.FirstName, CreatorLastName = story.LastName,
            CreatorUsername = story.CreatorUsername, CreatorProfileMediaPublicId = story.CreatorProfileMediaPublicId,
            StoryContentTypePublicId = story.StoryContentTypePublicId, StoryContentTypeTitle = story.StoryContentTypeTitle, StoryContentTypePersianTitle = story.StoryContentTypePersianTitle,
            ContentMediaPublicId = story.ContentMediaPublicId, PreviewMediaPublicId = story.PreviewMediaPublicId, Title = story.Title, ExpireDateTime = story.ExpireDateTime,
            ViewedByMe = viewedByMe, LikedByMe = likedByMe, ViewsCount = viewsCount, LikesCount = likesCount, Widgets = widgets,
        } };
    }
}
