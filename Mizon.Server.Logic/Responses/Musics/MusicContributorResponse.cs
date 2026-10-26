namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicContributorsResponse : IApiResponse
{
    public List<MusicContributorItemResponse> Contributors { get; set; } = [];
}

public sealed class MusicContributorItemResponse
{
    public Guid PersonPublicId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public Guid? ProfileMediaPublicId { get; set; }

    public Guid MusicContributorTypePublicId { get; set; }
    public string MusicContributorTypeTitle { get; set; } = null!;
    public string? MusicContributorTypePersianTitle { get; set; }
}
