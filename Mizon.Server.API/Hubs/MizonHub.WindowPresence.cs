using Microsoft.AspNetCore.SignalR;

namespace Mizon.Server.API.Hubs;

public partial class MizonHub
{
    private const string WindowPresenceKey = "Mizon.WindowPresence";

    /// <summary>Presence of this authenticated connection's main window, not all of the user's devices.</summary>
    public async Task<ClientWindowPresenceResponse> SetWindowPresence(bool isActive, long revision)
    {
        Context.ConnectionAborted.ThrowIfCancellationRequested();
        var clientPublicId = RequirePresenceAccount();
        if (revision <= 0)
            throw new HubException("The presence revision must be positive.");

        ClientWindowPresenceResponse response;
        ClientAccountPresenceResponse? accountChange = null;
        lock (Context.Items)
        {
            Context.ConnectionAborted.ThrowIfCancellationRequested();
            Context.Items.TryGetValue(WindowPresenceKey, out var value);
            var previous = value as ClientWindowPresenceResponse;
            if (previous is not null && previous.Revision >= revision)
                return previous;

            response = new()
            {
                ClientPublicId = clientPublicId,
                ConnectionId = Context.ConnectionId,
                IsOnline = isActive,
                Revision = revision,
            };
            Context.Items[WindowPresenceKey] = response;
            if ((previous?.IsOnline ?? false) != isActive)
                accountChange = UpdateAccountWindow(clientPublicId, Context.ConnectionId, isActive);
        }

        if (accountChange is not null)
            await PublishAccountPresenceAsync(accountChange);

        // Keep this acknowledgement tied to the calling window for existing clients.
        await Clients.Caller.SendAsync("WindowPresenceChanged", response, Context.ConnectionAborted);
        return response;
    }
}
