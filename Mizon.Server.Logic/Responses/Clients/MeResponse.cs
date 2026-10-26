namespace Mizon.Server.Logic.Responses.Clients;

public class MeResponse : IApiResponse
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Username { get; set; }
    public string? NickName { get; set; }
    public string EmailAddress { get; set; } = null!;
    public bool EmailAddressIsVerified { get; set; }
    public MeProfileResponse? MeProfile { get; set; }
}

public sealed class MeProfileResponse
{
    public Guid MediaPublicId { get; set; }
    public long FileSize { get; set; }
    public string Blurhash { get; set; } = null!;
    public short Width { get; set; }
    public short Height { get; set; }
    public string PrimaryAccentColor { get; set; } = null!;
}
