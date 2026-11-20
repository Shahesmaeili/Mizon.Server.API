using Microsoft.AspNetCore.SignalR;
using Mizon.Server.Logic.Operations.Clients;

namespace Mizon.Server.API.Hubs;

public sealed record OnlineClientCount(int Count, long Revision);

public partial class MizonHub
{
    private static readonly Lock OnlineClientsGate = new();
    private static readonly Dictionary<Guid, int> OnlineClients = [];
    private static readonly Dictionary<string, Guid> OnlineConnections = new(StringComparer.Ordinal);
    private static OnlineClientCount _latestOnlineCount = new(0, 0);

    public OnlineClientCount GetOnlineClientCount() => Volatile.Read(ref _latestOnlineCount);

    public Task<int> GetOnlineFollowerCount()
    {
        if (Context.User?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(Context.User.FindFirst("sub")?.Value, out var account) || account == Guid.Empty)
            throw new HubException("An authenticated client is required.");

        HashSet<Guid> online;
        lock (OnlineClientsGate) online = [.. OnlineClients.Keys];
        online.Remove(account);
        return new OnlineFollowerCountOperation(_mizonDatabaseContext)
            .CountAsync(account, online, Context.ConnectionAborted);
    }

    private async Task RegisterOnlineConnectionAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(Context.User.FindFirst("sub")?.Value, out var account) || account == Guid.Empty)
            return;

        OnlineClientCount? changed = null;
        lock (OnlineClientsGate)
        {
            if (!OnlineConnections.TryAdd(Context.ConnectionId, account)) return;
            OnlineClients.TryGetValue(account, out var devices);
            OnlineClients[account] = devices + 1;
            if (devices == 0)
                changed = PublishOnlineCount();
        }
        if (changed is not null)
            await BroadcastOnlineCountAsync(changed);
    }

    private async Task UnregisterOnlineConnectionAsync()
    {
        OnlineClientCount? changed = null;
        lock (OnlineClientsGate)
        {
            if (!OnlineConnections.Remove(Context.ConnectionId, out var account)) return;
            if (OnlineClients[account] > 1)
                OnlineClients[account]--;
            else
            {
                OnlineClients.Remove(account);
                changed = PublishOnlineCount();
            }
        }
        if (changed is not null)
            await BroadcastOnlineCountAsync(changed);
    }

    private static OnlineClientCount PublishOnlineCount()
    {
        var current = new OnlineClientCount(OnlineClients.Count, _latestOnlineCount.Revision + 1);
        Volatile.Write(ref _latestOnlineCount, current);
        return current;
    }

    private async Task BroadcastOnlineCountAsync(OnlineClientCount count)
    {
        try { await Clients.All.SendAsync("OnlineClientCountChanged", count); }
        catch (Exception error) { _logger.LogWarning(error, "Could not broadcast the online client count."); }
    }
}
