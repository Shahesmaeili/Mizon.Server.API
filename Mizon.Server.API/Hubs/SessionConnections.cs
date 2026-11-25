using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Mizon.Server.API.Hubs;

internal sealed class SessionConnections(IHubContext<MizonHub> hub, ILogger<SessionConnections> logger) : IClientSessionEvents
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Action>> _connections = [];

    private static string AccountGroup(Guid account) => $"Mizon.Sessions:{account:N}";
    private static string SessionGroup(Guid session) => $"Mizon.Session:{session:N}";

    internal async Task JoinAsync(Guid account, Guid session, HubCallerContext context, IGroupManager groups)
    {
        await groups.AddToGroupAsync(context.ConnectionId, AccountGroup(account), context.ConnectionAborted);
        await groups.AddToGroupAsync(context.ConnectionId, SessionGroup(session), context.ConnectionAborted);
        lock (_gate)
        {
            context.ConnectionAborted.ThrowIfCancellationRequested();
            if (!_connections.TryGetValue(session, out var connections))
                _connections[session] = connections = new(StringComparer.Ordinal);
            connections[context.ConnectionId] = context.Abort;
        }
        await ChangedAsync(account, []);
    }

    internal async Task LeaveAsync(Guid account, Guid session, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(session, out var connections) || !connections.Remove(connectionId) ||
                connections.Count > 0) return;
            _connections.Remove(session);
        }
        await ChangedAsync(account, []);
    }

    public bool IsOnline(Guid sessionPublicId)
    {
        lock (_gate) return _connections.ContainsKey(sessionPublicId);
    }

    public async Task ChangedAsync(Guid clientPublicId, IReadOnlyList<Guid> revokedSessions)
    {
        await Task.WhenAll(revokedSessions.Select(RevokeAsync));
        await NotifyAsync(AccountGroup(clientPublicId), "SessionsChanged");
    }

    public Task UnconfirmedAsync(Guid clientPublicId) =>
        NotifyAsync(AccountGroup(clientPublicId), "NewSessionUnconfirmed");

    private async Task RevokeAsync(Guid session)
    {
        try { await NotifyAsync(SessionGroup(session), "SessionRevoked"); }
        finally
        {
            Dictionary<string, Action>? connections;
            lock (_gate) _connections.Remove(session, out connections);
            if (connections is not null)
                foreach (var abort in connections.Values) abort();
        }
    }

    private async Task NotifyAsync(string group, string eventName)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await hub.Clients.Group(group).SendAsync(eventName, timeout.Token);
        }
        catch (Exception error) { logger.LogWarning(error, "Could not send a session notification."); }
    }
}

internal sealed class ActiveSessionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var context = invocation.Context;
        if (!Guid.TryParse(context.User?.FindFirstValue("sub"), out var account) ||
            !Guid.TryParse(context.User?.FindFirstValue("sid"), out var session) ||
            !await invocation.ServiceProvider.GetRequiredService<IClientSessionAuthorization>()
                .ValidateAsync(account, session, true, context.ConnectionAborted))
        {
            context.Abort();
            throw new HubException("The session is no longer active.");
        }
        return await next(invocation);
    }
}
