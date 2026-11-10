namespace Mizon.Server.Logic.Responses.Clients;

public sealed class ClientMoodItemResponse
{
    public Guid PublicId { get; set; }
    public string Emoji { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
    public Guid AnimationMediaPublicId { get; set; }
    public Guid? PreviewMediaPublicId { get; set; }
    public List<Guid> EffectMediaPublicIds { get; set; } = [];
}

