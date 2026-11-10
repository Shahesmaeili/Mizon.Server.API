using System.Globalization;

namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicLibraryOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicLibraryRequest, MusicLibraryResponse>
{
    public async Task<BaseApiResponse<MusicLibraryResponse>> HandleAsync(
        BaseApiRequest<MusicLibraryRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var content = request.Content ?? new();
        var pageSize = Math.Clamp(content.PageSize, 1, 50);
        DateTimeOffset beforeActivity = default;
        var beforeId = 0;
        if (content.Cursor is not null && !TryReadCursor(content.Cursor, out beforeActivity, out beforeId))
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicPlaylistCursor } };

        var ownedPlaylists = mizonDatabaseContext.MusicPlaylists.AsNoTracking()
            .Where(x => x.ClientId == clientId);

        // The index is (ClientId, LastActivityDateTime DESC, Id DESC). There is no
        // OFFSET scan and no collection materialization for a large library.
        var pageQuery = ownedPlaylists;
        if (content.Cursor is not null)
        {
            pageQuery = pageQuery
                .Where(x => x.LastActivityDateTime <= beforeActivity)
                .Where(x => x.LastActivityDateTime < beforeActivity || x.Id < beforeId);
        }

        var rows = await pageQuery
            .OrderByDescending(x => x.LastActivityDateTime)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.PublicId, x.Title, x.LastActivityDateTime })
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var ids = rows.Select(x => x.Id).ToArray();

        var counts = ids.Length == 0 ? new Dictionary<int, int>() : await mizonDatabaseContext.MusicPlaylistMusics.AsNoTracking()
            .Where(x => ids.Contains(x.MusicPlaylistId))
            .GroupBy(x => x.MusicPlaylistId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var covers = ids.Length == 0 ? new Dictionary<int, Guid?>() : await ownedPlaylists
            .Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                MediaPublicId = x.MusicPlaylistMusics
                    .OrderByDescending(item => item.Id)
                    .Select(item => item.Music!.MusicCover == null ? (Guid?)null : item.Music.MusicCover.Media!.PublicId)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.Id, x => x.MediaPublicId, cancellationToken);

        var totalPlaylistsCount = await ownedPlaylists.CountAsync(cancellationToken);
        var totalMusicsCount = await mizonDatabaseContext.MusicPlaylistMusics.AsNoTracking()
            .Where(x => x.MusicPlaylist!.ClientId == clientId)
            .LongCountAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                TotalPlaylistsCount = totalPlaylistsCount,
                TotalMusicsCount = totalMusicsCount,
                Playlists = rows.Select(x => new MusicLibraryPlaylistItemResponse
                {
                    PublicId = x.PublicId,
                    Title = x.Title,
                    LastUpdatedAt = x.LastActivityDateTime,
                    MusicsCount = counts.GetValueOrDefault(x.Id),
                    LastMusicCoverMediaPublicId = covers.GetValueOrDefault(x.Id)
                }).ToList(),
                NextCursor = hasMore ? CreateCursor(rows[^1].LastActivityDateTime, rows[^1].Id) : null
            }
        };
    }

    private static string CreateCursor(DateTimeOffset activity, int id) =>
        FormattableString.Invariant($"{activity.UtcTicks}:{id}");

    private static bool TryReadCursor(string cursor, out DateTimeOffset activity, out int id)
    {
        activity = default;
        id = 0;
        if (cursor.Length > 40) return false;
        var separator = cursor.IndexOf(':');
        if (separator < 1) return false;
        if (!long.TryParse(cursor.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return false;
        if (!int.TryParse(cursor.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0) return false;
        activity = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}
