namespace Mizon.Server.API.Endpoints;

public interface IEndpoint
{
    void Map(IEndpointRouteBuilder endpointRouteBuilder);
}
