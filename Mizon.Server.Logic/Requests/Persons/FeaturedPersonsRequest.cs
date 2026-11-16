namespace Mizon.Server.Logic.Requests.Persons;

public sealed class FeaturedPersonsRequest : IApiRequest
{
    public string? TypeCode { get; init; }
}
