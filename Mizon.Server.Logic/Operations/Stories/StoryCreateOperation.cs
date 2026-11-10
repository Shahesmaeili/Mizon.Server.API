namespace Mizon.Server.Logic.Operations.Stories;

public sealed class StoryCreateOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<StoryCreateRequest, StoryResponse>
{
    public async Task<BaseApiResponse<StoryResponse>> HandleAsync(BaseApiRequest<StoryCreateRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var input = request.Content!;
        if (input.Title?.Length > 60)
            return new() { Error = new() { Code = 11002 } };

        var contentMediaId = await mizonDatabaseContext.Medias.Where(x => x.PublicId == input.ContentMediaPublicId).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        var previewMediaId = input.PreviewMediaPublicId is null ? null : await mizonDatabaseContext.Medias.Where(x => x.PublicId == input.PreviewMediaPublicId).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        var contentTypeId = await mizonDatabaseContext.StoryContentTypes.Where(x => x.PublicId == input.StoryContentTypePublicId).Select(x => (byte?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (contentMediaId is null || contentTypeId is null || (input.PreviewMediaPublicId is not null && previewMediaId is null))
            return new() { Error = new() { Code = 11001 } };

        var story = new Story
        {
            ClientId = requestMetadata.ClientId.Value,
            ContentMediaId = contentMediaId.Value,
            PreviewMediaId = previewMediaId,
            StoryContentTypeId = contentTypeId.Value,
            Title = input.Title,
            ExpireDateTime = input.ExpireDateTime,
        };

        // Keep each lookup separate, but fetch its references once for the whole
        // request. Validation below retains the original widget/error order.
        var typePublicIds = input.Widgets.Select(x => x.StoryWidgetTypePublicId).Distinct().ToArray();
        var types = typePublicIds.Length == 0 ? [] : await mizonDatabaseContext.StoryWidgetTypes.AsNoTracking()
            .Where(x => typePublicIds.Contains(x.PublicId))
            .Select(x => new { x.PublicId, x.Id })
            .ToDictionaryAsync(x => x.PublicId, x => x.Id, cancellationToken);
        var stylePublicIds = input.Widgets.Select(x => x.StoryWidgetStylePublicId).Distinct().ToArray();
        var styles = stylePublicIds.Length == 0 ? [] : await mizonDatabaseContext.StoryWidgetStyles.AsNoTracking()
            .Where(x => stylePublicIds.Contains(x.PublicId))
            .Select(x => new
            {
                x.PublicId,
                x.Id,
                x.StoryWidgetTypeId,
            })
            .ToDictionaryAsync(x => x.PublicId, cancellationToken);
        var musicPublicIds = input.Widgets
            .Where(x => x.MusicPublicId.HasValue)
            .Select(x => x.MusicPublicId!.Value)
            .Distinct()
            .ToArray();
        var musics = musicPublicIds.Length == 0 ? [] : await mizonDatabaseContext.Musics.AsNoTracking()
            .Where(x => musicPublicIds.Contains(x.PublicId))
            .Select(x => new { x.PublicId, x.Id })
            .ToDictionaryAsync(x => x.PublicId, x => x.Id, cancellationToken);
        var clientPublicIds = input.Widgets
            .Where(x => x.ClientPublicId.HasValue)
            .Select(x => x.ClientPublicId!.Value)
            .Distinct()
            .ToArray();
        var clients = clientPublicIds.Length == 0 ? [] : await mizonDatabaseContext.Clients.AsNoTracking()
            .Where(x => clientPublicIds.Contains(x.PublicId))
            .Select(x => new { x.PublicId, x.Id })
            .ToDictionaryAsync(x => x.PublicId, x => x.Id, cancellationToken);

        foreach (var widgetInput in input.Widgets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (widgetInput.Width <= 0 || widgetInput.Height <= 0)
                return new() { Error = new() { Code = 11002 } };

            if (!types.TryGetValue(widgetInput.StoryWidgetTypePublicId, out var widgetTypeId)
                || !styles.TryGetValue(widgetInput.StoryWidgetStylePublicId, out var style)
                || style.StoryWidgetTypeId != widgetTypeId)
                return new() { Error = new() { Code = 11002 } };

            var widget = new StoryWidget
            {
                StoryWidgetTypeId = style.StoryWidgetTypeId,
                StoryWidgetStyleId = style.Id,
                X = widgetInput.X, Y = widgetInput.Y, Width = widgetInput.Width, Height = widgetInput.Height,
                Rotation = widgetInput.Rotation, ZIndex = widgetInput.ZIndex,
            };

            if (style.StoryWidgetTypeId == 2 && widgetInput.MusicPublicId is Guid musicPublicId)
            {
                if (!musics.TryGetValue(musicPublicId, out var musicId))
                    return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
                widget.MusicWidget = new() { MusicId = musicId };
            }
            else if (style.StoryWidgetTypeId == 1 && widgetInput.ClientPublicId is Guid clientPublicId)
            {
                if (!clients.TryGetValue(clientPublicId, out var clientId))
                    return new() { Error = new() { Code = 11003 } };
                widget.ClientWidget = new() { ClientId = clientId };
            }
            else return new() { Error = new() { Code = 11002 } };

            story.StoryWidgets.Add(widget);
        }

        mizonDatabaseContext.Stories.Add(story);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        return new() { Content = new() { PublicId = story.PublicId } };
    }
}
