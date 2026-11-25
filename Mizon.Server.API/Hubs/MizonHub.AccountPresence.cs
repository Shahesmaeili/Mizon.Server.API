using Microsoft.AspNetCore.SignalR;

namespace Mizon.Server.API.Hubs;

public partial class MizonHub
{
    private const string AccountPresenceChangedEvent = "AccountPresenceChanged";
    private static readonly Lock AccountPresenceGate = new();
    private static readonly Dictionary<Guid, ActiveAccount> ActiveAccounts = [];
    private static long _accountPresenceRevision;

    private sealed record ActiveAccount(HashSet<string> Connections, long Revision);

    /// <summary>Subscribe to the authenticated account's combined window presence.</summary>
    public async Task<ClientAccountPresenceResponse> WatchAccountPresence()
    {
        Context.ConnectionAborted.ThrowIfCancellationRequested();
        var account = RequirePresenceAccount();
        await Groups.AddToGroupAsync(Context.ConnectionId, AccountPresenceGroup(account), Context.ConnectionAborted);

        // Join before taking the snapshot. If an event overtakes this response, its revision
        // tells the client which state is newer.
        lock (AccountPresenceGate)
            return ActiveAccounts.TryGetValue(account, out var active)
                ? new(account, true, active.Revision)
                : new(account, false, _accountPresenceRevision);
    }

    private static ClientAccountPresenceResponse? UpdateAccountWindow(Guid account, string connectionId, bool active)
    {
        lock (AccountPresenceGate)
        {
            if (!ActiveAccounts.TryGetValue(account, out var windows))
            {
                if (!active) return null;
                var revision = ++_accountPresenceRevision;
                ActiveAccounts.Add(account, new(new(StringComparer.Ordinal) { connectionId }, revision));
                return new(account, true, revision);
            }

            if (active)
            {
                windows.Connections.Add(connectionId);
                return null;
            }

            if (!windows.Connections.Remove(connectionId) || windows.Connections.Count != 0)
                return null;

            ActiveAccounts.Remove(account);
            return new(account, false, ++_accountPresenceRevision);
        }
    }

    private async Task CloseWindowPresenceAsync()
    {
        ClientAccountPresenceResponse? changed = null;
        lock (Context.Items)
        {
            if (Context.Items.Remove(WindowPresenceKey, out var value)
                && value is ClientWindowPresenceResponse { IsOnline: true } previous)
            {
                changed = UpdateAccountWindow(previous.ClientPublicId, Context.ConnectionId, false);
            }
        }

        if (changed is not null)
            await PublishAccountPresenceAsync(changed);
    }

    private async Task PublishAccountPresenceAsync(ClientAccountPresenceResponse response)
    {
        try
        {
            await Clients.Group(AccountPresenceGroup(response.ClientPublicId))
                .SendAsync(AccountPresenceChangedEvent, response);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Could not broadcast account window presence.");
        }
    }

    private Guid RequirePresenceAccount()
    {
        if (Context.User?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(Context.User.FindFirst("sub")?.Value, out var account)
            || account == Guid.Empty)
        {
            throw new HubException("An authenticated client is required.");
        }

        return account;
    }

    private static string AccountPresenceGroup(Guid account) => $"Mizon.AccountPresence:{account:N}";
}
