namespace Mizon.Server.Logic.Operations.Clients;

public interface IClientSessionEvents
{
    bool IsOnline(Guid sessionPublicId);
    Task ChangedAsync(Guid clientPublicId, IReadOnlyList<Guid> revokedSessions);
    Task UnconfirmedAsync(Guid clientPublicId);
}

public sealed class ClientSessionsOperation(MizonDatabaseContext database, IClientSessionEvents events)
    : IApiOperation<ClientSessionsRequest, ClientSessionsResponse>
{
    public async Task<BaseApiResponse<ClientSessionsResponse>> HandleAsync(
        BaseApiRequest<ClientSessionsRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        if (metadata is not { ClientId: int clientId, SessionPublicId: Guid current })
            return ClientSessionMutations.Failure<ClientSessionsResponse>(401, "Unauthorized");

        var inactivityDays = await database.Clients.AsNoTracking()
            .Where(client => client.Id == clientId)
            .Select(client => client.ClientSessionInactivityDays)
            .SingleAsync(cancellationToken) ?? 0;
        var now = DateTimeOffset.UtcNow;
        var cutoff = inactivityDays > 0 ? now.AddDays(-inactivityDays) : DateTimeOffset.MinValue;
        var confirmationCutoff = now.AddHours(-24);
        var sessions = await database.ClientSessions.AsNoTracking()
            .Where(session => session.ClientId == clientId && session.LogoutDateTime == null && session.LastSeenDateTime > cutoff)
            .OrderByDescending(session => session.PublicId == current)
            .ThenByDescending(session => session.LastSeenDateTime)
            .ThenByDescending(session => session.Id)
            .Select(session => new ClientSessionItemResponse(
                session.PublicId, session.PlatformVersion.Platform!.Title, session.PlatformVersion.Version,
                session.OsVersion, session.DeviceName, session.DeviceModel, session.CreateDateTime,
                session.LastSeenDateTime, session.PublicId == current, false,
                session.ClientSessionIps.OrderByDescending(item => item.Id).Select(item => item.IpAddress).FirstOrDefault(),
                null, session.IsConfirmationPending && session.CreateDateTime > confirmationCutoff))
            .ToListAsync(cancellationToken);
        if (!sessions.Any(session => session.IsCurrent))
            return ClientSessionMutations.Failure<ClientSessionsResponse>(401, "Unauthorized");

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            sessions[index] = session with
            {
                CreatedAtUtc = session.CreatedAtUtc.ToUniversalTime(),
                LastActiveAtUtc = session.LastActiveAtUtc.ToUniversalTime(),
                IsOnline = events.IsOnline(session.SessionPublicId),
            };
        }
        return new() { Content = new(sessions, inactivityDays) };
    }
}

public sealed class TerminateSessionOperation(ClientSessionMutations mutations)
    : IApiOperation<TerminateSessionRequest, TerminateSessionsResponse>
{
    public Task<BaseApiResponse<TerminateSessionsResponse>> HandleAsync(
        BaseApiRequest<TerminateSessionRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken) =>
        request.Content is { SessionPublicId: var target } && target != Guid.Empty
            ? mutations.TerminateAsync(metadata, target, cancellationToken)
            : Task.FromResult(ClientSessionMutations.Failure<TerminateSessionsResponse>(4600, "Invalid session"));
}

public sealed class TerminateOtherSessionsOperation(ClientSessionMutations mutations)
    : IApiOperation<TerminateOtherSessionsRequest, TerminateSessionsResponse>
{
    public Task<BaseApiResponse<TerminateSessionsResponse>> HandleAsync(
        BaseApiRequest<TerminateOtherSessionsRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken) =>
        mutations.TerminateAsync(metadata, null, cancellationToken);
}

public sealed class UpdateSessionInactivityOperation(MizonDatabaseContext database, IClientSessionEvents events)
    : IApiOperation<UpdateSessionInactivityRequest, UpdateSessionInactivityResponse>
{
    public async Task<BaseApiResponse<UpdateSessionInactivityResponse>> HandleAsync(
        BaseApiRequest<UpdateSessionInactivityRequest> request, RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        if (metadata is not { ClientId: int clientId, ClientPublicId: Guid account, SessionPublicId: Guid current })
            return ClientSessionMutations.Failure<UpdateSessionInactivityResponse>(401, "Unauthorized");
        if (request.Content?.InactivityDays is not (0 or 7 or 30 or 90 or 180 or 365))
            return ClientSessionMutations.Failure<UpdateSessionInactivityResponse>(4604, "Invalid inactivity period");

        var days = request.Content.InactivityDays;
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var currentDays = await database.Clients.AsNoTracking().Where(client => client.Id == clientId)
            .Select(client => client.ClientSessionInactivityDays).SingleAsync(cancellationToken) ?? 0;
        var changed = await database.Clients
            .Where(client => client.Id == clientId)
            .ExecuteUpdateAsync(update => update.SetProperty(client => client.ClientSessionInactivityDays, days)
                .SetProperty(client => client.UpdateDateTime, now), cancellationToken);
        if (changed == 0) return ClientSessionMutations.Failure<UpdateSessionInactivityResponse>(401, "Unauthorized");
        // Record expiry under the older policy before it can be relaxed.
        var effective = currentDays == 0 ? days : days == 0 ? currentDays : Math.Min(days, currentDays);
        Guid[] expired = [];
        if (effective > 0)
        {
            var cutoff = now.AddDays(-effective);
            var stale = database.ClientSessions.Where(session => session.ClientId == clientId && session.PublicId != current &&
                session.LogoutDateTime == null && session.LastSeenDateTime <= cutoff);
            expired = await stale.Select(session => session.PublicId).ToArrayAsync(cancellationToken);
            if (expired.Length > 0)
                await stale.Where(session => expired.Contains(session.PublicId))
                    .ExecuteUpdateAsync(update => update.SetProperty(session => session.LogoutDateTime, now)
                        .SetProperty(session => session.UpdateDateTime, now), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await events.ChangedAsync(account, expired);
        return new() { Content = new(days) };
    }
}

public sealed class ClientSessionMutations(MizonDatabaseContext database, IClientSessionEvents events)
{
    public async Task<BaseApiResponse<TerminateSessionsResponse>> TerminateAsync(
        RequestMetadata? metadata, Guid? target, CancellationToken cancellationToken)
    {
        if (metadata is not { ClientId: int clientId, ClientPublicId: Guid account, SessionPublicId: Guid current })
            return Failure<TerminateSessionsResponse>(401, "Unauthorized");
        if (target == current) return Failure<TerminateSessionsResponse>(4601, "Use sign out for this device");

        var now = DateTimeOffset.UtcNow;
        var currentSession = await database.ClientSessions.AsNoTracking()
            .Where(session => session.ClientId == clientId && session.PublicId == current && session.LogoutDateTime == null)
            .Select(session => new { session.CreateDateTime })
            .SingleOrDefaultAsync(cancellationToken);
        if (currentSession is null) return Failure<TerminateSessionsResponse>(401, "Unauthorized");

        var otherSessions = database.ClientSessions
            .Where(session => session.ClientId == clientId && session.PublicId != current && session.LogoutDateTime == null);
        if (target is Guid sessionPublicId) otherSessions = otherSessions.Where(session => session.PublicId == sessionPublicId);
        var ids = await otherSessions.AsNoTracking()
            .Select(session => session.PublicId).ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
            return target.HasValue
                ? Failure<TerminateSessionsResponse>(4602, "Session not found")
                : new() { Content = new(0) };

        if (currentSession.CreateDateTime > now.AddHours(-24))
            return Failure<TerminateSessionsResponse>(4603, "Use an older session to sign out other devices");

        var count = await otherSessions.Where(session => ids.Contains(session.PublicId))
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.LogoutDateTime, now)
                .SetProperty(session => session.UpdateDateTime, now), cancellationToken);
        if (count > 0) await events.ChangedAsync(account, ids);
        return new() { Content = new(count) };
    }

    public static BaseApiResponse<T> Failure<T>(int code, string message) where T : IApiResponse =>
        new() { Error = new BaseApiError { Code = code, Title = message, Details = message } };
}

public sealed class ConfirmSessionOperation(MizonDatabaseContext database, IClientSessionEvents events)
    : IApiOperation<ConfirmSessionRequest, ConfirmSessionResponse>
{
    public async Task<BaseApiResponse<ConfirmSessionResponse>> HandleAsync(
        BaseApiRequest<ConfirmSessionRequest> request, RequestMetadata? metadata, CancellationToken token)
    {
        if (metadata is not { ClientId: int clientId, ClientPublicId: Guid account, SessionPublicId: Guid current })
            return ClientSessionMutations.Failure<ConfirmSessionResponse>(401, "Unauthorized");
        if (request.Content is not { SessionPublicId: var target } || target == Guid.Empty || target == current)
            return ClientSessionMutations.Failure<ConfirmSessionResponse>(4600, "Invalid session");

        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddHours(-24);
        var affected = await database.ClientSessions.Where(session => session.ClientId == clientId &&
                session.PublicId == target && session.LogoutDateTime == null && session.IsConfirmationPending &&
                database.ClientSessions.Any(actor => actor.ClientId == clientId && actor.PublicId == current &&
                    actor.LogoutDateTime == null && (!actor.IsConfirmationPending || actor.CreateDateTime <= deadline)))
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.IsConfirmationPending, false)
                .SetProperty(session => session.UpdateDateTime, now), token);
        if (affected == 0)
            return ClientSessionMutations.Failure<ConfirmSessionResponse>(4602, "Session could not be confirmed");

        await events.ChangedAsync(account, []);
        return new() { Content = new(true) };
    }
}
