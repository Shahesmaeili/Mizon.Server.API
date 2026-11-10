using System.Globalization;

namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicPlaylistDetailsOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicPlaylistDetailsRequest, MusicPlaylistDetailsResponse>
{
    public async Task<BaseApiResponse<MusicPlaylistDetailsResponse>> HandleAsync(
        BaseApiRequest<MusicPlaylistDetailsRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var content = request.Content ?? new();
        var pageSize = Math.Clamp(content.PageSize, 1, 100);
        var beforeId = int.MaxValue;
        if (content.Cursor is not null)
        {
            if (!int.TryParse(content.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out beforeId) || beforeId <= 0)
                return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicPlaylistCursor } };
        }

        var playlist = await mizonDatabaseContext.MusicPlaylists.AsNoTracking()
            .Where(x => x.PublicId == content.MusicPlaylistPublicId)
            .Select(x => new { x.Id, x.PublicId, x.Title, x.ClientId })
            .FirstOrDefaultAsync(cancellationToken);

        if (playlist is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicPlaylistNotFound } };

        var isOwner = playlist.ClientId == clientId;
        if (!isOwner)
        {
            var followsOwner = await mizonDatabaseContext.ClientFollows.AsNoTracking()
                .Where(x => x.FollowerClientId == clientId)
                .Where(x => x.FollowedClientId == playlist.ClientId)
                .Where(x => x.FollowDateTime != null)
                .Where(x => x.EndDateTime == null)
                .AnyAsync(cancellationToken);
            if (!followsOwner)
                return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicPlaylistNotFound } };
        }

        var memberships = mizonDatabaseContext.MusicPlaylistMusics.AsNoTracking()
            .Where(x => x.MusicPlaylistId == playlist.Id)
            .Where(x => x.Music!.IsActive);
        var count = await memberships.CountAsync(cancellationToken);
        var rows = await memberships
            .Where(x => x.Id < beforeId)
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.MusicId,
                x.Music!.PublicId,
                x.Music.Title,
                x.Music.PersianTitle,
                CoverMediaPublicId = x.Music.MusicCover == null ? (Guid?)null : x.Music.MusicCover.Media!.PublicId
            })
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0)
        {
            return new()
            {
                Content = new()
                {
                    PublicId = playlist.PublicId,
                    Title = playlist.Title,
                    IsOwner = isOwner,
                    MusicsCount = count
                }
            };
        }

        var musicIds = rows.Select(x => x.MusicId).ToArray();
        var likes = await mizonDatabaseContext.MusicLikes.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .GroupBy(x => x.MusicId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var contributors = await mizonDatabaseContext.MusicContributors.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .OrderBy(x => x.Id)
            .Select(x => new { x.MusicId, x.PersonId, x.Person!.FirstName, x.Person.LastName })
            .Distinct()
            .ToListAsync(cancellationToken);
        var contributorsByMusic = contributors.GroupBy(x => x.MusicId)
            .ToDictionary(group => group.Key, group => group.Select(x => new MusicTabContributorItemResponse
            {
                FirstName = x.FirstName,
                LastName = x.LastName
            }).ToList());

        return new()
        {
            Content = new()
            {
                PublicId = playlist.PublicId,
                Title = playlist.Title,
                IsOwner = isOwner,
                MusicsCount = count,
                Musics = rows.Select(x => new MusicTabMusicItemResponse
                {
                    PublicId = x.PublicId,
                    Title = x.Title,
                    PersianTitle = x.PersianTitle,
                    CoverMediaPublicId = x.CoverMediaPublicId,
                    LikesCount = likes.GetValueOrDefault(x.MusicId),
                    Contributors = contributorsByMusic.GetValueOrDefault(x.MusicId) ?? []
                }).ToList(),
                NextCursor = hasMore ? rows[^1].Id.ToString(CultureInfo.InvariantCulture) : null
            }
        };
    }
}
