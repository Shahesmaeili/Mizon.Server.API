namespace Mizon.Server.Logic.Requests.Persons;

public sealed class PersonMusicsRequest : IApiRequest
{
    public Guid PersonPublicId { get; set; }
    public Guid? BeforeMusicPublicId { get; set; }
}
