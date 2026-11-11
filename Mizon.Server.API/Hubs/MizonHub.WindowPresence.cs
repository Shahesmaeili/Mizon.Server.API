using Microsoft.AspNetCore.SignalR;

namespace Mizon.Server.API.Hubs;

public partial class MizonHub
{
    private const string WindowPresenceKey = "Mizon.WindowPresence";

    /// <summary>Presence of this authenticated connection's main window, not all of the user's devices.</summary>
    public async Task<ClientWindowPresenceResponse> SetWindowPresence(bool isActive, long revision)
    {
        Context.ConnectionAborted.ThrowIfCancellationRequested();
        if (Context.User?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(Context.User.FindFirst("sub")?.Value, out var clientPublicId)
            || clientPublicId == Guid.Empty)
        {
            throw new HubException("An authenticated client is required.");
        }
        if (revision <= 0)
            throw new HubException("The presence revision must be positive.");

        ClientWindowPresenceResponse response;
        lock (Context.Items)
        {
            Context.ConnectionAborted.ThrowIfCancellationRequested();
            if (Context.Items.TryGetValue(WindowPresenceKey, out var value)
                && value is ClientWindowPresenceResponse previous
                && previous.Revision >= revision)
            {
                return previous;
            }

            response = new()
            {
                ClientPublicId = clientPublicId,
                ConnectionId = Context.ConnectionId,
                IsOnline = isActive,
                Revision = revision,
            };
            Context.Items[WindowPresenceKey] = response;
        }

        // This is an acknowledgement to the owner, never a public/global activity broadcast.
        await Clients.Caller.SendAsync("WindowPresenceChanged", response, Context.ConnectionAborted);
        return response;
    }
}
