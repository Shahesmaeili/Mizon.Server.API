namespace Mizon.Server.Logic.Requests.Clients;

public sealed class RegisterRequest : IApiRequest
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string EmailAddress { get; set; } = null!;
    public string Password { get; set; } = null!;
}
