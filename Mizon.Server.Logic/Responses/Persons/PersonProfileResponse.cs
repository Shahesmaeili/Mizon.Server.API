namespace Mizon.Server.Logic.Responses.Persons;

public sealed class PersonProfileResponse : IApiResponse
{
    public Guid PersonPublicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid? ProfileMediaPublicId { get; set; }
    public Guid? CoverMediaPublicId { get; set; }
    public Guid? ModelMediaPublicId { get; set; }
    public float? ModelYawInDegrees { get; set; }
    public int FollowersCount { get; set; }
    public int? FollowingCount { get; set; }
    public int MusicsCount { get; set; }
    public bool IsFollowing { get; set; }
    public bool CanFollow { get; set; }
}
