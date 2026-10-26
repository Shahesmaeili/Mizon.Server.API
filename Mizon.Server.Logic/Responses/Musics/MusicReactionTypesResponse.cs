namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicReactionTypesResponse : IApiResponse
{
    public List<MusicReactionTypeItemResponse> MusicReactionTypes { get; set; } = [];
}


public sealed class MusicReactionTypeItemResponse
{
    public string Emoji { get; set; } = null!;
    public Guid PublicId { get; set; }
}