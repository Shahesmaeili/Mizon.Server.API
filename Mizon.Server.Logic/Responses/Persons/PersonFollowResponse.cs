namespace Mizon.Server.Logic.Responses.Persons;

public sealed class PersonFollowResponse : IApiResponse
{
    public bool IsFollowing { get; set; }
    public int FollowersCount { get; set; }
}
