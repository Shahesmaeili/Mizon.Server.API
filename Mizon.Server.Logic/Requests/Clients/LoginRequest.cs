namespace Mizon.Server.Logic.Requests.Clients;

/// <summary>
/// Represents request to login operation.
/// </summary>
public class LoginRequest : IApiRequest
{
    /// <summary>
    /// Username for the login operation.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Email address for the login operation.
    /// </summary>
    public string? EmailAddress { get; set; }

    /// <summary>
    /// password for the login operation.
    /// </summary>
    public string Password { get; set; } = null!;
}
