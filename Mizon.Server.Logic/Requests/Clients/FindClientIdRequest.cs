namespace Mizon.Server.Logic.Requests.Clients;

public sealed class FindClientIdRequest : IApiRequest
{
    public Guid ClientPublicId { get; set; }
}
