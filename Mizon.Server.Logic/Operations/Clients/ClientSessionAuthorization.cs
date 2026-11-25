namespace Mizon.Server.Logic.Operations.Clients;

public interface IClientSessionAuthorization
{
    Task<bool> ValidateAsync(Guid clientPublicId, Guid sessionPublicId, bool trackActivity, CancellationToken cancellationToken);
}

public sealed class ClientSessionAuthorization(MizonDatabaseContext database) : IClientSessionAuthorization
{
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromMinutes(1);

    public async Task<bool> ValidateAsync(Guid clientPublicId, Guid sessionPublicId, bool trackActivity, CancellationToken cancellationToken)
    {
        if (clientPublicId == Guid.Empty || sessionPublicId == Guid.Empty) return false;

        var now = DateTimeOffset.UtcNow;
        var session = await database.ClientSessions.AsNoTracking()
            .Where(session => session.PublicId == sessionPublicId && session.Client!.PublicId == clientPublicId &&
                session.LogoutDateTime == null)
            .Select(session => new { session.Id, session.LastSeenDateTime, session.Client!.ClientSessionInactivityDays })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null) return false;
        if (session.ClientSessionInactivityDays is > 0 and var days && session.LastSeenDateTime.AddDays(days) <= now)
        {
            var expirationCutoff = now.AddDays(-days);
            await database.ClientSessions.Where(row => row.Id == session.Id && row.LogoutDateTime == null &&
                    row.Client!.ClientSessionInactivityDays == days && row.LastSeenDateTime <= expirationCutoff)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.LogoutDateTime, now)
                    .SetProperty(row => row.UpdateDateTime, now), cancellationToken);
            return false;
        }

        var activityThreshold = now - ActivityInterval;
        if (trackActivity && session.LastSeenDateTime < activityThreshold)
        {
            await database.ClientSessions
                .Where(row => row.Id == session.Id && row.LogoutDateTime == null &&
                    row.LastSeenDateTime < activityThreshold)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.LastSeenDateTime, now), cancellationToken);
        }
        return true;
    }
}
