namespace Mizon.Server.Logic.Operations.Clients;

public sealed class OnlineFollowerCountOperation(MizonDatabaseContext database)
{
    public async Task<int> CountAsync(Guid accountId, IReadOnlySet<Guid> onlineAccounts, CancellationToken token)
    {
        if (onlineAccounts.Count == 0) return 0;

        var followers = database.ClientFollows.AsNoTracking()
            .Where(follow => follow.FollowedClient != null)
            .Where(follow => follow.FollowedClient!.PublicId == accountId)
            .Where(follow => follow.FollowDateTime != null)
            .Where(follow => follow.EndDateTime == null)
            .Select(follow => follow.FollowerClient!.PublicId)
            .Distinct();

        var count = 0;
        await foreach (var follower in followers.AsAsyncEnumerable().WithCancellation(token))
            if (onlineAccounts.Contains(follower)) count++;
        return count;
    }
}
