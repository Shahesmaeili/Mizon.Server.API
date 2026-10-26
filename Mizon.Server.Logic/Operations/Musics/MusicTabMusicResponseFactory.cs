namespace Mizon.Server.Logic.Operations.Musics;

internal static class MusicTabMusicResponseFactory
{
    public static async Task<BaseApiResponse<MusicTabMusicsResponse>> CreateAsync(MizonDatabaseContext mizonDatabaseContext, List<int> musicIds, CancellationToken cancellationToken)
    {
        if (musicIds.Count == 0)
            return new() { Content = new() };

        var musicRows = await mizonDatabaseContext.Musics
            .AsNoTracking()
            .Where(m => musicIds.Contains(m.Id))
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                PublicId = m.PublicId,
                Title = m.Title,
                PersianTitle = m.PersianTitle,
                LikesCount = m.MusicLikes.Count,
                CoverMediaPublicId = m.MusicCover == null ? (Guid?)null : m.MusicCover.Media!.PublicId
            })
            .ToListAsync(cancellationToken);

        var contributors = await mizonDatabaseContext.MusicContributors
            .AsNoTracking()
            .Where(mc => musicIds.Contains(mc.MusicId))
            .OrderBy(mc => mc.Id)
            .Select(mc => new
            {
                mc.MusicId,
                mc.Person!.FirstName,
                mc.Person.LastName
            })
            .ToListAsync(cancellationToken);

        var contributorsByMusicId = contributors
            .GroupBy(contributor => contributor.MusicId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(contributor => new MusicTabContributorItemResponse
                {
                    FirstName = contributor.FirstName,
                    LastName = contributor.LastName
                }).ToList());

        var musics = musicRows.Select(music => new MusicTabMusicItemResponse
        {
            PublicId = music.PublicId,
            Title = music.Title,
            PersianTitle = music.PersianTitle,
            LikesCount = music.LikesCount,
            CoverMediaPublicId = music.CoverMediaPublicId,
            Contributors = contributorsByMusicId.GetValueOrDefault(music.Id, [])
        }).ToList();

        return new()
        {
            Content = new()
            {
                Musics = musics
            }
        };
    }
}
