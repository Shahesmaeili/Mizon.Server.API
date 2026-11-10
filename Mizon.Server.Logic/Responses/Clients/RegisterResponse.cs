namespace Mizon.Server.Logic.Responses.Clients;

public sealed class RegisterResponse : IApiResponse
{
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public Guid SessionPublicId { get; set; }
    public Guid ClientPublicId { get; set; }
    public int? ClientId { get; set; }
}
