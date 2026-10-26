namespace Mizon.Server.Logic.Requests.Stories;

public sealed class StoryCreateRequest : IApiRequest
{
    public Guid ContentMediaPublicId { get; set; }
    public Guid? PreviewMediaPublicId { get; set; }
    public Guid StoryContentTypePublicId { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset? ExpireDateTime { get; set; }
    public List<StoryWidgetCreateRequest> Widgets { get; set; } = [];
}

public sealed class StoryWidgetCreateRequest
{
    public Guid StoryWidgetTypePublicId { get; set; }
    public Guid StoryWidgetStylePublicId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Rotation { get; set; }
    public short ZIndex { get; set; }
    public Guid? MusicPublicId { get; set; }
    public Guid? ClientPublicId { get; set; }
}
