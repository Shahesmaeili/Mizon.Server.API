namespace Mizon.Server.Logic.Requests.Persons;

public sealed class PersonFollowRequest : IApiRequest
{
    public Guid PersonPublicId { get; set; }
    public bool? Following { get; set; }
}
