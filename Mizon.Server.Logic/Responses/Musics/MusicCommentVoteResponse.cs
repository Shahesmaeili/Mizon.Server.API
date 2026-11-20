namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicCommentVoteResponse : IApiResponse
{
    public int Likes { get; set; }
    public int Dislikes { get; set; }
    public int MyVote { get; set; }
}
