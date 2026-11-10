namespace Mizon.Server.Logic.Requests.Clients;

public sealed class ClientProfileRequest : IApiRequest
{
    public Guid ClientPublicId { get; set; }
}
