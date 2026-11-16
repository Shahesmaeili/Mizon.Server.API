using Mizon.Server.Logic.Responses.Musics;

namespace Mizon.Server.Logic.Responses.Persons;

public sealed class PersonMusicsResponse : IApiResponse
{
    public List<MusicTabMusicItemResponse> Musics { get; set; } = [];
    public Guid? NextBeforeMusicPublicId { get; set; }
}
