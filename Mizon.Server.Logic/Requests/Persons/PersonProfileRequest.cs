namespace Mizon.Server.Logic.Requests.Persons;

public sealed class PersonProfileRequest : IApiRequest
{
    public Guid PersonPublicId { get; set; }
}
