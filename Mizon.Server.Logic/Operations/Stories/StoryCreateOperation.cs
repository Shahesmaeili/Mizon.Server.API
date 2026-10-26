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

        foreach (var widgetInput in input.Widgets)
        {
            if (widgetInput.Width <= 0 || widgetInput.Height <= 0)
                return new() { Error = new() { Code = 11002 } };

            var widgetTypeId = await mizonDatabaseContext.StoryWidgetTypes.Where(x => x.PublicId == widgetInput.StoryWidgetTypePublicId).Select(x => (byte?)x.Id).FirstOrDefaultAsync(cancellationToken);
            var style = await mizonDatabaseContext.StoryWidgetStyles.Where(x => x.PublicId == widgetInput.StoryWidgetStylePublicId).Select(x => new { x.Id, x.StoryWidgetTypeId }).FirstOrDefaultAsync(cancellationToken);
            if (widgetTypeId is null || style is null || style.StoryWidgetTypeId != widgetTypeId.Value)
                return new() { Error = new() { Code = 11002 } };

            var widget = new StoryWidget
            {
                StoryWidgetTypeId = widgetTypeId.Value,
                StoryWidgetStyleId = style.Id,
                X = widgetInput.X, Y = widgetInput.Y, Width = widgetInput.Width, Height = widgetInput.Height,
                Rotation = widgetInput.Rotation, ZIndex = widgetInput.ZIndex,
            };

            if (widgetTypeId == 2 && widgetInput.MusicPublicId is Guid musicPublicId)
            {
                var musicId = await mizonDatabaseContext.Musics.Where(x => x.PublicId == musicPublicId).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
                if (musicId is null) return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };
                widget.MusicWidget = new() { MusicId = musicId.Value };
            }
            else if (widgetTypeId == 1 && widgetInput.ClientPublicId is Guid clientPublicId)
            {
                var clientId = await mizonDatabaseContext.Clients.Where(x => x.PublicId == clientPublicId).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
                if (clientId is null) return new() { Error = new() { Code = 11003 } };
                widget.ClientWidget = new() { ClientId = clientId.Value };
            }
            else return new() { Error = new() { Code = 11002 } };

            story.StoryWidgets.Add(widget);
        }

        mizonDatabaseContext.Stories.Add(story);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        return new() { Content = new() { PublicId = story.PublicId } };
    }
}
