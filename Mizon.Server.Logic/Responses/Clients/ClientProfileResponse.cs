namespace Mizon.Server.Logic.Responses.Clients;

public sealed class ClientProfileResponse : IApiResponse
{
    public Guid ClientPublicId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? NickName { get; set; }
    public string? Username { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }
    public string? PrimaryAccentColor { get; set; }
}
