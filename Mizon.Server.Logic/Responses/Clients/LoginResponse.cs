namespace Mizon.Server.Logic.Responses.Clients;

/// <summary>
/// Represents response from login operation.
/// </summary>
public class LoginResponse : IApiResponse
{
    /// <summary>
    /// The authentication token.
    /// </summary>
    public string AccessToken { get; set; } = null!;

    public string RefreshToken { get; set; } = null!;
    public Guid SessionPublicId { get; set; }
    public Guid ClientPublicId { get; set; }

    public int? ClientId { get; set; }
}
