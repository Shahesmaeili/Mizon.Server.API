namespace Mizon.Server.Logic.Responses.Search;

public sealed class GlobalSearchItemResponse : IApiResponse
{
    public string ItemType { get; set; } = null!;
    public Guid PublicId { get; set; }

    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NickName { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }

    public string? Title { get; set; }
    public string? PersianTitle { get; set; }
    public Guid? CoverMediaPublicId { get; set; }
}
