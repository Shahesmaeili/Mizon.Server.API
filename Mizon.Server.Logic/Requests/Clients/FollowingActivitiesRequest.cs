namespace Mizon.Server.Logic.Requests.Clients;

public sealed class FollowingActivitiesRequest : IApiRequest
{
    public string Scope { get; set; } = "all";
    public int PageSize { get; set; } = 20;
    public string? Cursor { get; set; }
}
