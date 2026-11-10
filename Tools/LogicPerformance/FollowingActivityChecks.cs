using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server;
using Mizon.Server.Logic.Operations.Clients;
using Mizon.Server.Logic.Requests.Clients;
using Mizon.Server.Logic.Responses.Clients;

internal static class FollowingActivityChecks
{
    public static async Task RunAsync(DbContextOptions<MizonDatabaseContext> options)
    {
        // The caller supplies SELECT-only guarded options. Fixtures are existing
        // clients/follows; the checks never seed or modify the user's database.
        await using var fixtures = new MizonDatabaseContext(options);
        var follower = await fixtures.ClientFollows.AsNoTracking()
            .Where(x => x.FollowDateTime != null)
            .Where(x => x.EndDateTime == null)
            .Where(x => x.FollowedClientId != null)
            .Where(x => x.FollowedClientId != x.FollowerClientId)
            .OrderBy(x => x.Id)
            .Select(x => new { Id = x.FollowerClientId, x.FollowerClient!.PublicId })
            .FirstOrDefaultAsync();

        if (follower is null)
            Console.WriteLine("SKIP following pagination: no existing active client-to-client follow.");
        else
        {
            var metadata = new RequestMetadata { ClientId = follower.Id, ClientPublicId = follower.PublicId };
            await CheckPagesAsync("music", 4, metadata);
            await CheckPagesAsync("all", 4, metadata);
            await CheckPagesAsync("all", 40, metadata);
        }

        var noFollowing = await fixtures.Clients.AsNoTracking()
            .Where(x => !fixtures.ClientFollows
                .Where(follow => follow.FollowerClientId == x.Id)
                .Where(follow => follow.FollowDateTime != null)
                .Where(follow => follow.EndDateTime == null).Any())
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.PublicId }).FirstOrDefaultAsync();
        if (noFollowing is null)
            Console.WriteLine("SKIP no-following state: no existing client without active followings.");
        else
        {
            var response = await ReadAsync("music", 4, null,
                new() { ClientId = noFollowing.Id, ClientPublicId = noFollowing.PublicId });
            Require(!response.HasFollowings && !response.HasMore && response.NextCursor is null
                && response.Activities.Count == 0, "no-following empty state");
            Console.WriteLine("PASS following no-following state using an existing client.");
        }

        async Task CheckPagesAsync(string scope, int pageSize, RequestMetadata metadata)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            FollowingActivityItemResponse? previous = null;
            string? cursor = null;
            var testedContinuation = false;
            for (var pageNumber = 0; pageNumber < 3; pageNumber++)
            {
                var page = await ReadAsync(scope, pageSize, cursor, metadata);
                var repeated = await ReadAsync(scope, pageSize, cursor, metadata);
                Require(page.HasFollowings, "active-follow state");
                Require(page.Activities.Count <= pageSize, "page size bound");
                Require(page.Activities.Select(x => x.ActivityId)
                    .SequenceEqual(repeated.Activities.Select(x => x.ActivityId))
                    && page.HasMore == repeated.HasMore && page.NextCursor == repeated.NextCursor,
                    "stable IDs/cursor for repeated page; run with no concurrent fixture changes");
                Require(page.HasMore == (page.NextCursor is not null), "continuation cursor presence");

                var actorIds = page.Activities.Select(x => x.ClientPublicId).Distinct().ToArray();
                var followedActors = actorIds.Length == 0 ? [] : await fixtures.ClientFollows.AsNoTracking()
                    .Where(x => x.FollowerClientId == metadata.ClientId)
                    .Where(x => x.FollowedClientId != null)
                    .Where(x => x.FollowDateTime != null)
                    .Where(x => x.EndDateTime == null)
                    .Where(x => actorIds.Contains(x.FollowedClient!.PublicId))
                    .Select(x => x.FollowedClient!.PublicId).ToHashSetAsync();
                foreach (var item in page.Activities)
                {
                    Require(seen.Add(item.ActivityId), "no duplicate activity across pages");
                    Require(item.ClientPublicId != metadata.ClientPublicId
                        && followedActors.Contains(item.ClientPublicId), "actor belongs to active followings and is not self");
                    if (previous is not null)
                        Require(Compare(previous, item) > 0, "newest-first order including equal timestamp ties");
                    previous = item;
                    Require(item.ActivityType switch
                    {
                        "comment" => item.TargetType == "music-comment" && item.MusicCommentPublicId.HasValue && item.MusicPublicId.HasValue,
                        "like" or "reaction" or "play" => item.TargetType == "music" && item.MusicPublicId.HasValue,
                        "playlist-created" => item.TargetType == "music-playlist" && item.MusicPlaylistPublicId.HasValue,
                        "story-posted" => item.TargetType == "story" && item.StoryPublicId.HasValue,
                        _ => false
                    }, "activity target routing");
                    if (scope == "music")
                    {
                        Require(item.Domain == "music", "music widget excludes other domains");
                        if (item.ActivityType == "story-posted")
                            Require(item.TaggedMusicsCount > 0, "music story has a current music tag");
                    }
                }
                if (!page.HasMore) break;
                Require(page.NextCursor != cursor, "cursor advances");
                cursor = page.NextCursor;
                testedContinuation = true;
            }
            Console.WriteLine($"PASS following {scope}/{pageSize}: {seen.Count} distinct activities, ownership, routing, ordering and stable cursors.");
            if (!testedContinuation)
                Console.WriteLine($"SKIP following {scope}/{pageSize} continuation: existing fixture fits one page.");
        }

        async Task<FollowingActivitiesResponse> ReadAsync(string scope, int pageSize, string? cursor, RequestMetadata metadata)
        {
            await using var database = new MizonDatabaseContext(options);
            var response = await new FollowingActivitiesOperation(database).HandleAsync(
                new BaseApiRequest<FollowingActivitiesRequest>
                {
                    Content = new() { Scope = scope, PageSize = pageSize, Cursor = cursor }
                }, metadata, CancellationToken.None);
            Require(response.Error is null && response.Content is not null, "successful read");
            Require(!database.ChangeTracker.Entries().Any(), "no tracked read entities");
            return response.Content!;
        }
    }

    private static int Compare(FollowingActivityItemResponse left, FollowingActivityItemResponse right)
    {
        var dateComparison = left.CreateDateTime.CompareTo(right.CreateDateTime);
        if (dateComparison != 0) return dateComparison;
        var leftKey = left.ActivityId.Split(':');
        var rightKey = right.ActivityId.Split(':');
        var kindComparison = int.Parse(leftKey[0], CultureInfo.InvariantCulture)
            .CompareTo(int.Parse(rightKey[0], CultureInfo.InvariantCulture));
        return kindComparison != 0 ? kindComparison
            : int.Parse(leftKey[1], CultureInfo.InvariantCulture).CompareTo(int.Parse(rightKey[1], CultureInfo.InvariantCulture));
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Following activity regression: " + label);
    }
}
