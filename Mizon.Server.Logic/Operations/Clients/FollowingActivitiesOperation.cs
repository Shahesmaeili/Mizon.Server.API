using System.Globalization;

namespace Mizon.Server.Logic.Operations.Clients;

public sealed class FollowingActivitiesOperation(MizonDatabaseContext database)
    : IApiOperation<FollowingActivitiesRequest, FollowingActivitiesResponse>
{
    private const int Comment = 1;
    private const int Like = 2;
    private const int Reaction = 3;
    private const int Play = 4;
    private const int PlaylistCreated = 5;
    private const int StoryPosted = 6;

    public async Task<BaseApiResponse<FollowingActivitiesResponse>> HandleAsync(
        BaseApiRequest<FollowingActivitiesRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var content = request.Content ?? new();
        if (content.Scope is not ("all" or "music"))
            return InvalidRequest();
        if (!ActivityCursor.TryParse(content.Cursor, content.Scope, out var cursor))
            return InvalidRequest();

        var pageSize = Math.Clamp(content.PageSize, 1, 40);
        var followings = database.ClientFollows.AsNoTracking()
            .Where(x => x.FollowerClientId == clientId)
            .Where(x => x.FollowDateTime != null)
            .Where(x => x.EndDateTime == null);
        var hasFollowings = await followings.AnyAsync(cancellationToken);
        if (!hasFollowings)
            return new() { Content = new() };

        var followedClientIds = followings
            .Where(x => x.FollowedClientId != null)
            .Where(x => x.FollowedClientId != clientId)
            .Select(x => x.FollowedClientId!.Value);

        // Each source is bounded in SQL. Only the final page is enriched with names,
        // media identifiers and text; the number of followings is never materialized.
        var rows = new List<ActivityRow>((pageSize + 1) * 6);
        await AppendAsync(database.MusicComments.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Where(x => x.Music!.IsActive)
            .Select(x => new ActivityRow
            {
                Id = x.Id, Kind = Comment, ClientId = x.ClientId,
                MusicId = x.MusicId, CreateDateTime = x.CreateDateTime
            }), Comment);
        await AppendAsync(database.MusicLikes.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Where(x => x.Music!.IsActive)
            .Select(x => new ActivityRow
            {
                Id = x.Id, Kind = Like, ClientId = x.ClientId,
                MusicId = x.MusicId, CreateDateTime = x.CreateDateTime
            }), Like);
        await AppendAsync(database.MusicReactions.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Where(x => x.Music!.IsActive)
            .Select(x => new ActivityRow
            {
                Id = x.Id, Kind = Reaction, ClientId = x.ClientId,
                MusicId = x.MusicId, CreateDateTime = x.CreateDateTime
            }), Reaction);
        await AppendAsync(database.MusicPlays.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Where(x => x.Music!.IsActive)
            .Select(x => new ActivityRow
            {
                Id = x.Id, Kind = Play, ClientId = x.ClientId,
                MusicId = x.MusicId, CreateDateTime = x.CreateDateTime
            }), Play);
        await AppendAsync(database.MusicPlaylists.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Select(x => new ActivityRow
            {
                Id = x.Id, Kind = PlaylistCreated, ClientId = x.ClientId,
                PlaylistId = x.Id, CreateDateTime = x.CreateDateTime
            }), PlaylistCreated);

        var now = DateTimeOffset.UtcNow;
        var storyQuery = database.Stories.AsNoTracking()
            .Where(x => followedClientIds.Contains(x.ClientId))
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > now);
        if (content.Scope == "music")
            storyQuery = storyQuery.Where(x => x.StoryWidgets
                .Where(widget => widget.MusicWidget != null)
                .Any(widget => widget.MusicWidget!.Music!.IsActive));
        // Query stories themselves so several music tags still produce one activity.
        await AppendAsync(storyQuery.Select(x => new ActivityRow
        {
            Id = x.Id, Kind = StoryPosted, ClientId = x.ClientId,
            StoryId = x.Id, CreateDateTime = x.CreateDateTime, ExpireDateTime = x.ExpireDateTime
        }), StoryPosted);

        // Untagged stories belong to the shared feed, but never the music widget.
        var page = rows.OrderByDescending(x => x.CreateDateTime)
            .ThenByDescending(x => x.Kind)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .ToList();
        var hasMore = page.Count > pageSize;
        if (hasMore) page.RemoveAt(page.Count - 1);
        if (page.Count == 0)
            return new() { Content = new() { HasFollowings = true } };

        var actorIds = page.Select(x => x.ClientId).Distinct().ToArray();
        var actors = await database.Clients.AsNoTracking()
            .Where(x => actorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.FirstName, x.LastName, x.NickName })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        // Choose one current row per actor in SQL, without materializing their
        // username/profile history or changing the latest-Id selection rule.
        var usernamesByClient = await database.Clients.AsNoTracking()
            .Where(x => actorIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                Username = x.ClientUsernames.Where(username => username.IsActive)
                    .OrderByDescending(username => username.Id)
                    .Select(username => username.Username).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.Id, x => x.Username, cancellationToken);
        var profilesByClient = await database.Clients.AsNoTracking()
            .Where(x => actorIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                MediaPublicId = x.ClientProfiles.Where(profile => profile.IsDefault)
                    .OrderByDescending(profile => profile.Id)
                    .Select(profile => (Guid?)profile.Media!.PublicId).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.Id, x => x.MediaPublicId, cancellationToken);

        var musicIds = page.Where(x => x.MusicId != null).Select(x => x.MusicId!.Value).Distinct().ToArray();
        // Pages commonly contain only one or two activity kinds. Do not send
        // empty IN queries for the other kinds, especially the three story reads.
        var musics = musicIds.Length == 0 ? [] : await database.Musics.AsNoTracking()
            .Where(x => musicIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.PublicId, x.Title, x.PersianTitle,
                CoverMediaPublicId = x.MusicCover == null ? (Guid?)null : x.MusicCover.Media!.PublicId
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var playlistIds = page.Where(x => x.PlaylistId != null).Select(x => x.PlaylistId!.Value).ToArray();
        var playlists = playlistIds.Length == 0 ? [] : await database.MusicPlaylists.AsNoTracking()
            .Where(x => playlistIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.PublicId, x.Title,
                MusicsCount = x.MusicPlaylistMusics.Where(item => item.Music!.IsActive).Count(),
                CoverMediaPublicId = x.MusicPlaylistMusics
                    .Where(item => item.Music!.IsActive)
                    .OrderByDescending(item => item.Id)
                    .Select(item => item.Music!.MusicCover == null ? (Guid?)null : item.Music.MusicCover.Media!.PublicId)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var commentIds = page.Where(x => x.Kind == Comment).Select(x => x.Id).ToArray();
        var comments = commentIds.Length == 0 ? [] : await database.MusicComments.AsNoTracking()
            .Where(x => commentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, Text = x.Text.Length > 180 ? x.Text.Substring(0, 180) : x.Text })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var reactionIds = page.Where(x => x.Kind == Reaction).Select(x => x.Id).ToArray();
        var reactions = reactionIds.Length == 0 ? [] : await database.MusicReactions.AsNoTracking()
            .Where(x => reactionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.MusicReactionType!.Emoji })
            .ToDictionaryAsync(x => x.Id, x => x.Emoji, cancellationToken);

        var storyIds = page.Where(x => x.StoryId != null).Select(x => x.StoryId!.Value).ToArray();
        var stories = storyIds.Length == 0 ? [] : await database.Stories.AsNoTracking()
            .Where(x => storyIds.Contains(x.Id))
            .Where(x => x.IsActive)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > now)
            .Select(x => new
            {
                x.Id, x.PublicId, x.Title,
                StoryContentTypeTitle = x.StoryContentType!.Title,
                // A video must not be passed to the client's image decoder.
                PreviewMediaPublicId = x.PreviewMedia != null ? (Guid?)x.PreviewMedia.PublicId
                    : x.StoryContentType.Title == "Image" ? x.ContentMedia!.PublicId : (Guid?)null
            }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var storyTags = storyIds.Length == 0 ? [] : await database.StoryMusicWidgets.AsNoTracking()
            .Where(x => storyIds.Contains(x.StoryWidget!.StoryId))
            .Where(x => x.Music!.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.StoryWidget!.StoryId, x.MusicId,
                CoverMediaPublicId = x.Music!.MusicCover == null ? (Guid?)null : x.Music.MusicCover.Media!.PublicId
            }).ToListAsync(cancellationToken);
        var tagsByStory = storyTags.GroupBy(x => x.StoryId).ToDictionary(x => x.Key, x => x.DistinctBy(tag => tag.MusicId).ToArray());
        var viewedStories = storyIds.Length == 0 ? [] : await database.StoryViews.AsNoTracking()
            .Where(x => storyIds.Contains(x.StoryId))
            .Where(x => x.ClientId == clientId)
            .Select(x => x.StoryId).ToHashSetAsync(cancellationToken);

        var activities = new List<FollowingActivityItemResponse>(page.Count);
        DateTimeOffset? cacheExpiresAt = null;
        foreach (var row in page)
        {
            if (!actors.TryGetValue(row.ClientId, out var actor)) continue;
            var item = new FollowingActivityItemResponse
            {
                ActivityId = string.Create(CultureInfo.InvariantCulture, $"{row.Kind}:{row.Id}"),
                ActivityType = row.Kind switch
                {
                    Comment => "comment", Like => "like", Reaction => "reaction",
                    Play => "play", PlaylistCreated => "playlist-created", StoryPosted => "story-posted", _ => string.Empty
                },
                TargetType = row.Kind switch
                {
                    Comment => "music-comment", PlaylistCreated => "music-playlist", StoryPosted => "story", _ => "music"
                },
                Domain = "music",
                CreateDateTime = row.CreateDateTime,
                ClientPublicId = actor.PublicId,
                FirstName = actor.FirstName, LastName = actor.LastName, NickName = actor.NickName,
                Username = usernamesByClient.GetValueOrDefault(row.ClientId),
                ProfileMediaPublicId = profilesByClient.GetValueOrDefault(row.ClientId),
                ReactionEmoji = row.Kind == Reaction ? reactions.GetValueOrDefault(row.Id) : null
            };
            if (row.Kind == Comment)
            {
                if (!comments.TryGetValue(row.Id, out var comment)) continue;
                item.MusicCommentPublicId = comment.PublicId;
                item.CommentText = comment.Text;
            }
            if (row.MusicId is int musicId)
            {
                if (!musics.TryGetValue(musicId, out var music)) continue;
                item.MusicPublicId = music.PublicId;
                item.Title = music.Title;
                item.PersianTitle = music.PersianTitle;
                item.CoverMediaPublicId = music.CoverMediaPublicId;
            }
            if (row.PlaylistId is int playlistId)
            {
                if (!playlists.TryGetValue(playlistId, out var playlist)) continue;
                item.MusicPlaylistPublicId = playlist.PublicId;
                item.Title = playlist.Title;
                item.CoverMediaPublicId = playlist.CoverMediaPublicId;
                item.PlaylistMusicsCount = playlist.MusicsCount;
            }
            if (row.StoryId is int storyId)
            {
                if (!stories.TryGetValue(storyId, out var story)) continue;
                if (row.ExpireDateTime is DateTimeOffset expiresAt
                    && (cacheExpiresAt is null || expiresAt < cacheExpiresAt.Value))
                    cacheExpiresAt = expiresAt;
                var tags = tagsByStory.GetValueOrDefault(storyId);
                item.StoryPublicId = story.PublicId;
                item.StoryViewedByMe = viewedStories.Contains(storyId);
                item.StoryContentTypeTitle = story.StoryContentTypeTitle;
                item.TaggedMusicsCount = tags?.Length ?? 0;
                item.Domain = item.TaggedMusicsCount > 0 ? "music" : "story";
                item.Title = story.Title ?? string.Empty;
                item.CoverMediaPublicId = story.PreviewMediaPublicId
                    ?? tags?.FirstOrDefault(tag => tag.CoverMediaPublicId != null)?.CoverMediaPublicId;
            }
            activities.Add(item);
        }

        return new()
        {
            CacheExpiresAt = cacheExpiresAt,
            Content = new()
            {
                HasFollowings = true,
                HasMore = hasMore,
                NextCursor = hasMore ? ActivityCursor.Format(content.Scope, page[^1]) : null,
                Activities = activities
            }
        };

        async Task AppendAsync(IQueryable<ActivityRow> query, int kind)
        {
            if (cursor is not null)
            {
                var before = cursor.CreateDateTime;
                if (kind > cursor.Kind)
                    query = query.Where(x => x.CreateDateTime < before);
                else if (kind < cursor.Kind)
                    query = query.Where(x => x.CreateDateTime <= before);
                else
                    query = query.Where(x => x.CreateDateTime <= before)
                        .Where(x => x.CreateDateTime < before || x.Id < cursor.Id);
            }
            rows.AddRange(await query.OrderByDescending(x => x.CreateDateTime)
                .ThenByDescending(x => x.Id)
                .Take(pageSize + 1)
                .ToListAsync(cancellationToken));
        }
    }

    private static BaseApiResponse<FollowingActivitiesResponse> InvalidRequest() => new()
    {
        Error = new() { Code = OperationErrorCodes.Clients.InvalidFollowingActivitiesRequest }
    };

    private sealed class ActivityRow
    {
        public int Id { get; init; }
        public int Kind { get; init; }
        public int ClientId { get; init; }
        public int? MusicId { get; init; }
        public int? PlaylistId { get; init; }
        public int? StoryId { get; init; }
        public DateTimeOffset CreateDateTime { get; init; }
        public DateTimeOffset? ExpireDateTime { get; init; }
    }

    private sealed record ActivityCursor(DateTimeOffset CreateDateTime, int Kind, int Id)
    {
        public static string Format(string scope, ActivityRow row) =>
            string.Create(CultureInfo.InvariantCulture, $"1:{scope}:{row.CreateDateTime.UtcTicks}:{row.Kind}:{row.Id}");

        public static bool TryParse(string? value, string scope, out ActivityCursor? cursor)
        {
            cursor = null;
            if (value is null) return true;
            if (value.Length > 100) return false;
            var parts = value.Split(':');
            if (parts.Length != 5 || parts[0] != "1" || parts[1] != scope) return false;
            if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
            if (ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks) return false;
            if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var kind)) return false;
            if (kind < Comment || kind > StoryPosted) return false;
            if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return false;
            cursor = new(new DateTimeOffset(ticks, TimeSpan.Zero), kind, id);
            return true;
        }
    }
}
