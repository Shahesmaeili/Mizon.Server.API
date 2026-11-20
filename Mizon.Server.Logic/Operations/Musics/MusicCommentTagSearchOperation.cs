namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentTagSearchOperation(MizonDatabaseContext database)
    : IApiOperation<MusicCommentTagSearchRequest, MusicCommentTagSearchResponse>
{
    public async Task<BaseApiResponse<MusicCommentTagSearchResponse>> HandleAsync(BaseApiRequest<MusicCommentTagSearchRequest> request,
        RequestMetadata? metadata, CancellationToken token)
    {
        if (request.Content is not { } input || !MusicCommentTags.IsSupported(input.Type) ||
            input.Query?.Trim() is not { Length: >= 2 and <= 80 } query)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };

        var candidates = input.Type switch
        {
            "person" => database.Persons.AsNoTracking()
                .Where(row => row.FirstName.StartsWith(query) || row.LastName.StartsWith(query) ||
                    (row.FirstName + " " + row.LastName).StartsWith(query))
                .OrderBy(row => row.FirstName).ThenBy(row => row.Id).Select(row => new MusicCommentTagItem("person", row.PublicId, row.FirstName + " " + row.LastName)),
            "client" => database.Clients.AsNoTracking()
                .Where(row => row.ClientUsernames.Any(name => name.IsActive && name.Username.StartsWith(query)) ||
                    row.FirstName.StartsWith(query) || row.LastName.StartsWith(query) || (row.FirstName + " " + row.LastName).StartsWith(query))
                .OrderBy(row => row.FirstName).ThenBy(row => row.Id).Select(row => new MusicCommentTagItem("client", row.PublicId, row.FirstName + " " + row.LastName)),
            "music" => database.Musics.AsNoTracking().Where(row => row.IsActive)
                .Where(row => row.Title.StartsWith(query) || (row.PersianTitle != null && row.PersianTitle.StartsWith(query)))
                .OrderBy(row => row.Title).ThenBy(row => row.Id)
                .Select(row => new MusicCommentTagItem("music", row.PublicId, row.PersianTitle ?? row.Title)),
            _ => database.MusicVideos.AsNoTracking().Where(row => row.IsActive && row.Music!.IsActive)
                .Where(row => row.Music!.Title.StartsWith(query) || (row.Music.PersianTitle != null && row.Music.PersianTitle.StartsWith(query)))
                .OrderBy(row => row.Music!.Title).ThenBy(row => row.Id)
                .Select(row => new MusicCommentTagItem("music-video", row.PublicId, row.Music!.PersianTitle ?? row.Music.Title, row.Music.PublicId))
        };
        return new() { Content = new() { Tags = await candidates.Take(12).ToListAsync(token) } };
    }
}
