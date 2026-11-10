namespace Mizon.Server.Logic.Operations.Stories;

internal static class StoryVisibility
{
    public static IQueryable<Story> VisibleTo(this IQueryable<Story> query,
        MizonDatabaseContext database, int? clientId)
    {
        var followedClientIds = database.ClientFollows.AsNoTracking()
            .Where(x => x.FollowerClientId == clientId)
            .Where(x => x.FollowedClientId != null)
            .Where(x => x.FollowDateTime != null)
            .Where(x => x.EndDateTime == null)
            .Select(x => x.FollowedClientId!.Value);
        // Accounts created before privacy existed retain their public behavior.
        return query.Where(x => x.Client!.ClientPrivacyTypeId == null
            || x.Client.ClientPrivacyType!.Title == "Public"
            || x.ClientId == clientId
            || followedClientIds.Contains(x.ClientId));
    }
}
