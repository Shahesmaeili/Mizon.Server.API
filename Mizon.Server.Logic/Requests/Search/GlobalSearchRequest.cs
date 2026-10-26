namespace Mizon.Server.Logic.Requests.Search;

public sealed class GlobalSearchRequest : IApiRequest
{
    public string Query { get; set; } = string.Empty;
}
