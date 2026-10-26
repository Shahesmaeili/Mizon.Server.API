namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicReactionsResponse : IApiResponse
{
    public List<MusicReactionItemResponse> MusicReactionItems { get; set; } = [];
}


public sealed class MusicReactionItemResponse
{
    public string Emoji { get; set; } = null!;
    public int Count { get; set; }
    public bool ReactedByMe { get; set; }
    public Guid MusicReactionTypePublicId { get; set; }
}