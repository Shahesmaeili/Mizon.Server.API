namespace Mizon.Server.Logic.Requests.Clients;

public sealed class ClientMoodRequest : IApiRequest
{
    public Guid? ClientMoodTypePublicId { get; set; }
}

