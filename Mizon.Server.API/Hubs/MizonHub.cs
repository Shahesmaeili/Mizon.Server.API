using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using System.Collections.Concurrent;

namespace Mizon.Server.API.Hubs;

[Authorize]
public class MizonHub : Hub
{
    private static readonly HashSet<string> _connectedDevices = new();

    // groupName -> set of connection ids
    private static readonly ConcurrentDictionary<string, HashSet<string>> _groupConnections = new();

    // connectionId -> (entityType, entityPublicId)
    private static readonly ConcurrentDictionary<string, (string EntityType, Guid EntityId)> _connectionEntities = new();

    // client public id (if authenticated) -> (entityType, entityPublicId)
    private static readonly ConcurrentDictionary<string, (string EntityType, Guid EntityId)> _clientCurrentEntity = new();

    // Ephemeral playback state remains inside the existing entity group; no extra hub or database table is needed.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, MusicListenerState>> _musicListeners = new();
    private static readonly ConcurrentDictionary<string, Guid> _connectionClientPublicIds = new();
    private static readonly ConcurrentDictionary<Guid, MusicListenerIdentity> _musicListenerIdentities = new();

    private readonly ILogger<MizonHub> _logger;
    private readonly MizonDatabaseContext _mizonDatabaseContext;

    public MizonHub(ILogger<MizonHub> logger, MizonDatabaseContext mizonDatabaseContext)
    {
        _logger = logger;
        _mizonDatabaseContext = mizonDatabaseContext;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Hub connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            await RemoveConnectionFromCurrentEntity(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing connection from entity on disconnect");
        }

        _logger.LogInformation("Hub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task RegisterDevice(string token)
    {
        try
        {
            var deviceId = Context.UserIdentifier ?? Context.ConnectionId;

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("RegisterDevice called with empty token for device {DeviceId}", deviceId);
                throw new HubException("Invalid token");
            }

            _connectedDevices.Add(deviceId);
            Context.Items["DeviceRegistered"] = true;
            Context.Items["RegisteredAt"] = DateTime.UtcNow;

            _logger.LogInformation("Device registered: {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            throw new HubException($"Registration failed: {ex.Message}");
        }
    }

    public async Task UnregisterDevice()
    {
        try
        {
            var deviceId = Context.UserIdentifier ?? Context.ConnectionId;

            if (_connectedDevices.Remove(deviceId))
            {
                Context.Items.Remove("DeviceRegistered");
                Context.Items.Remove("RegisteredAt");

                _logger.LogInformation("Device unregistered: {DeviceId}", deviceId);

                await Clients.Caller.SendAsync("UnregistrationConfirmed", deviceId);
            }
            else
            {
                _logger.LogWarning("UnregisterDevice called for non-registered device: {DeviceId}", deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering device");
            throw new HubException($"Unregistration failed: {ex.Message}");
        }
    }

    // Client calls this when it navigates to a page showing a particular entity.
    // entityType: e.g., "Music", "Movie", "Series" etc.
    // entityPublicId: the public id (Guid) of the entity being viewed
    public async Task JoinEntity(string entityType, Guid entityPublicId)
    {
        try
        {
            var connectionId = Context.ConnectionId;

            // remove from previous entity if any
            await RemoveConnectionFromCurrentEntity(connectionId);

            var groupName = GetGroupName(entityType, entityPublicId);

            // add connection to group set
            var set = _groupConnections.GetOrAdd(groupName, _ => new HashSet<string>());
            lock (set)
            {
                set.Add(connectionId);
            }

            _connectionEntities[connectionId] = (entityType, entityPublicId);

            // also store by client public id if available
            var clientPublicId = Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(clientPublicId))
            {
                _clientCurrentEntity[clientPublicId] = (entityType, entityPublicId);

                if (Guid.TryParse(clientPublicId, out var parsedClientPublicId))
                    _connectionClientPublicIds[connectionId] = parsedClientPublicId;
            }

            await Groups.AddToGroupAsync(connectionId, groupName);

            var count = GetGroupCount(groupName);

            // broadcast updated count to group
            await Clients.Group(groupName).SendAsync("EntityViewersCountUpdated", entityType, entityPublicId, count);

            if (string.Equals(entityType, "Music", StringComparison.OrdinalIgnoreCase))
            {
                await Clients.Caller.SendAsync(
                    "MusicListenersSnapshot",
                    entityPublicId,
                    GetMusicListeners(groupName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JoinEntity error");
            throw new HubException(ex.Message);
        }
    }

    public async Task LeaveEntity()
    {
        try
        {
            var connectionId = Context.ConnectionId;
            await RemoveConnectionFromCurrentEntity(connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LeaveEntity error");
            throw new HubException(ex.Message);
        }
    }

    /// <summary>
    /// Updates a listener in the existing Music:{id} group. The state is ephemeral and is
    /// intentionally not persisted: it only represents what a connected client is doing now.
    /// </summary>
    public async Task UpdateMusicPlayback(Guid musicPublicId, long positionMilliseconds, bool isPlaying)
    {
        if (!TryGetCurrentMusicGroup(musicPublicId, out var groupName))
            return;

        if (!TryGetCurrentClientPublicId(out var clientPublicId))
            return;

        var identity = await GetMusicListenerIdentityAsync(clientPublicId, Context.ConnectionAborted);
        if (identity is null)
            return;

        var listeners = _musicListeners.GetOrAdd(groupName, _ => new());
        var state = new MusicListenerState(
            Context.ConnectionId,
            identity,
            Math.Max(0, positionMilliseconds),
            isPlaying,
            DateTimeOffset.UtcNow);

        listeners[clientPublicId] = state;

        await Clients.Group(groupName).SendAsync(
            "MusicListenerUpdated",
            musicPublicId,
            ToResponse(clientPublicId, state));
    }

    private async Task RemoveConnectionFromCurrentEntity(string connectionId)
    {
        if (_connectionEntities.TryRemove(connectionId, out var tuple))
        {
            var groupName = GetGroupName(tuple.EntityType, tuple.EntityId);

            if (_groupConnections.TryGetValue(groupName, out var set))
            {
                lock (set)
                {
                    set.Remove(connectionId);
                }

                var count = GetGroupCount(groupName);
                await Clients.Group(groupName).SendAsync("EntityViewersCountUpdated", tuple.EntityType, tuple.EntityId, count);
            }

            await Groups.RemoveFromGroupAsync(connectionId, groupName);

            if (_connectionClientPublicIds.TryRemove(connectionId, out var listenerClientPublicId)
                && _musicListeners.TryGetValue(groupName, out var listeners)
                && listeners.TryGetValue(listenerClientPublicId, out var listener)
                && string.Equals(listener.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                listeners.TryRemove(listenerClientPublicId, out _);
                await Clients.Group(groupName).SendAsync("MusicListenerRemoved", tuple.EntityId, listenerClientPublicId);
            }

            // remove from clientCurrentEntity by client public id if matches
            var clientPublicId = Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(clientPublicId))
            {
                _clientCurrentEntity.TryRemove(clientPublicId, out _);
            }
        }
    }

    private static int GetGroupCount(string groupName)
    {
        if (_groupConnections.TryGetValue(groupName, out var set))
            return set.Count;

        return 0;
    }

    private static string GetGroupName(string entityType, Guid entityId) => $"{entityType}:{entityId}";

    private bool TryGetCurrentMusicGroup(Guid musicPublicId, out string groupName)
    {
        groupName = GetGroupName("Music", musicPublicId);

        return _connectionEntities.TryGetValue(Context.ConnectionId, out var entity)
            && string.Equals(entity.EntityType, "Music", StringComparison.OrdinalIgnoreCase)
            && entity.EntityId == musicPublicId;
    }

    private bool TryGetCurrentClientPublicId(out Guid clientPublicId)
    {
        if (_connectionClientPublicIds.TryGetValue(Context.ConnectionId, out clientPublicId))
            return true;

        return Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out clientPublicId);
    }

    private async Task<MusicListenerIdentity?> GetMusicListenerIdentityAsync(Guid clientPublicId, CancellationToken cancellationToken)
    {
        if (_musicListenerIdentities.TryGetValue(clientPublicId, out var cached))
            return cached;

        var client = await _mizonDatabaseContext.Clients
            .AsNoTracking()
            .Where(x => x.PublicId == clientPublicId)
            .Select(x => new { x.Id, x.FirstName, x.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (client is null)
            return null;

        var username = await _mizonDatabaseContext.ClientUsernames
            .AsNoTracking()
            .Where(x => x.ClientId == client.Id)
            .Where(x => x.IsActive)
            .Select(x => x.Username)
            .FirstOrDefaultAsync(cancellationToken);

        var profileMediaPublicId = await _mizonDatabaseContext.ClientProfiles
            .AsNoTracking()
            .Where(x => x.ClientId == client.Id)
            .Where(x => x.IsDefault)
            .Select(x => (Guid?)x.Media!.PublicId)
            .FirstOrDefaultAsync(cancellationToken);

        var identity = new MusicListenerIdentity(client.FirstName, client.LastName, username, profileMediaPublicId);
        _musicListenerIdentities.TryAdd(clientPublicId, identity);
        return identity;
    }

    private static List<MusicListenerPresenceResponse> GetMusicListeners(string groupName)
    {
        if (!_musicListeners.TryGetValue(groupName, out var listeners))
            return [];

        return listeners
            .OrderBy(x => x.Key)
            .Select(x => ToResponse(x.Key, x.Value))
            .ToList();
    }

    private static MusicListenerPresenceResponse ToResponse(Guid clientPublicId, MusicListenerState state) => new()
    {
        ClientPublicId = clientPublicId,
        FirstName = state.Identity.FirstName,
        LastName = state.Identity.LastName,
        Username = state.Identity.Username,
        ProfileMediaPublicId = state.Identity.ProfileMediaPublicId,
        PositionMilliseconds = state.PositionMilliseconds,
        IsPlaying = state.IsPlaying,
        UpdatedAtUtc = state.UpdatedAtUtc,
    };

    private sealed record MusicListenerIdentity(
        string FirstName,
        string LastName,
        string? Username,
        Guid? ProfileMediaPublicId);

    private sealed record MusicListenerState(
        string ConnectionId,
        MusicListenerIdentity Identity,
        long PositionMilliseconds,
        bool IsPlaying,
        DateTimeOffset UpdatedAtUtc);
}
