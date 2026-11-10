using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using System.Collections.Concurrent;

namespace Mizon.Server.API.Hubs;

[Authorize]
public class MizonHub : Hub
{
    private const string DeviceRegisteredKey = "DeviceRegistered";
    private const string RegisteredAtKey = "RegisteredAt";
    private const string ListenerIdentityKey = "Mizon.MusicListenerIdentity";

    // One synchronization gate per entity keeps unrelated songs independent.
    private static readonly ConcurrentDictionary<string, EntityGroup> _entityGroups = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConnectionEntity> _connectionEntities = new(StringComparer.Ordinal);

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
        finally
        {
            Context.Items.Remove(DeviceRegisteredKey);
            Context.Items.Remove(RegisteredAtKey);
            Context.Items.Remove(ListenerIdentityKey);
        }

        _logger.LogInformation("Hub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task RegisterDevice(string token)
    {
        var deviceId = Context.UserIdentifier ?? Context.ConnectionId;

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("RegisterDevice called with empty token for device {DeviceId}", deviceId);
            throw new HubException("Registration failed: Invalid token");
        }

        // Authentication belongs to the hub middleware; registration belongs to this connection.
        Context.Items[DeviceRegisteredKey] = true;
        Context.Items[RegisteredAtKey] = DateTime.UtcNow;
        _logger.LogInformation("Device registered: {DeviceId}", deviceId);
        return Task.CompletedTask;
    }

    public async Task UnregisterDevice()
    {
        try
        {
            var deviceId = Context.UserIdentifier ?? Context.ConnectionId;

            if (Context.Items.Remove(DeviceRegisteredKey))
            {
                Context.Items.Remove(RegisteredAtKey);
                _logger.LogInformation("Device unregistered: {DeviceId}", deviceId);
                await Clients.Caller.SendAsync("UnregistrationConfirmed", deviceId, Context.ConnectionAborted);
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
    public async Task JoinEntity(string entityType, Guid entityPublicId)
    {
        try
        {
            var connectionId = Context.ConnectionId;
            await RemoveConnectionFromCurrentEntity(connectionId);

            var groupName = GetGroupName(entityType, entityPublicId);
            await Groups.AddToGroupAsync(connectionId, groupName, Context.ConnectionAborted);

            Guid? clientPublicId = Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var parsedClientPublicId)
                ? parsedClientPublicId
                : null;

            AddConnection(groupName, connectionId, new(entityType, entityPublicId, clientPublicId));
            Context.ConnectionAborted.ThrowIfCancellationRequested();

            await Clients.Group(groupName).SendAsync(
                "EntityViewersCountUpdated", entityType, entityPublicId, GetGroupCount(groupName));

            if (string.Equals(entityType, "Music", StringComparison.OrdinalIgnoreCase))
            {
                await Clients.Caller.SendAsync(
                    "MusicListenersSnapshot",
                    entityPublicId,
                    GetMusicListeners(groupName),
                    Context.ConnectionAborted);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await RemoveConnectionFromCurrentEntity(Context.ConnectionId);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException, "Error cleaning up failed entity join");
            }

            _logger.LogError(ex, "JoinEntity error");
            throw new HubException(ex.Message);
        }
    }

    public async Task LeaveEntity()
    {
        try
        {
            await RemoveConnectionFromCurrentEntity(Context.ConnectionId);
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
        if (!TryGetCurrentMusicGroup(musicPublicId, out var groupName, out var clientPublicId))
            return;

        var identity = await GetMusicListenerIdentityAsync(clientPublicId, Context.ConnectionAborted);
        if (identity is null)
            return;

        if (!_entityGroups.TryGetValue(groupName, out var group))
            return;

        var state = new MusicListenerState(
            Context.ConnectionId,
            identity,
            Math.Max(0, positionMilliseconds),
            isPlaying,
            DateTimeOffset.UtcNow);

        lock (group.Gate)
        {
            // The identity lookup may have completed after this connection left the page.
            if (!group.Connections.Contains(Context.ConnectionId))
                return;
            if (!TryGetCurrentMusicGroup(musicPublicId, out _, out var currentClientPublicId))
                return;
            if (currentClientPublicId != clientPublicId)
                return;

            group.Listeners[clientPublicId] = state;
        }

        await Clients.Group(groupName).SendAsync(
            "MusicListenerUpdated",
            musicPublicId,
            ToResponse(clientPublicId, state));
    }

    private static void AddConnection(string groupName, string connectionId, ConnectionEntity entity)
    {
        while (true)
        {
            var group = _entityGroups.GetOrAdd(groupName, static _ => new());
            lock (group.Gate)
            {
                // The last listener may have removed this group while we were waiting for its gate.
                if (!_entityGroups.TryGetValue(groupName, out var currentGroup))
                    continue;
                if (!ReferenceEquals(currentGroup, group))
                    continue;

                group.Connections.Add(connectionId);
                _connectionEntities[connectionId] = entity;
                return;
            }
        }
    }

    private async Task RemoveConnectionFromCurrentEntity(string connectionId)
    {
        if (!_connectionEntities.TryRemove(connectionId, out var entity))
            return;

        var groupName = GetGroupName(entity.EntityType, entity.EntityId);
        Guid? removedListenerPublicId = null;

        if (_entityGroups.TryGetValue(groupName, out var group))
        {
            lock (group.Gate)
            {
                group.Connections.Remove(connectionId);

                if (entity.ClientPublicId is Guid clientPublicId
                    && group.Listeners.TryGetValue(clientPublicId, out var listener)
                    && string.Equals(listener.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    group.Listeners.Remove(clientPublicId);
                    removedListenerPublicId = clientPublicId;
                }

                if (group.Connections.Count == 0)
                    _entityGroups.TryRemove(new KeyValuePair<string, EntityGroup>(groupName, group));
            }
        }

        // Release owned state before any transport operation, including a failed/disconnected send.
        await Groups.RemoveFromGroupAsync(connectionId, groupName);
        await Clients.Group(groupName).SendAsync(
            "EntityViewersCountUpdated", entity.EntityType, entity.EntityId, GetGroupCount(groupName));

        if (removedListenerPublicId is Guid removedClientPublicId)
            await Clients.Group(groupName).SendAsync("MusicListenerRemoved", entity.EntityId, removedClientPublicId);
    }

    private static int GetGroupCount(string groupName)
    {
        if (!_entityGroups.TryGetValue(groupName, out var group))
            return 0;

        lock (group.Gate)
            return group.Connections.Count;
    }

    private static string GetGroupName(string entityType, Guid entityId)
    {
        var canonicalType = string.Equals(entityType, "Music", StringComparison.OrdinalIgnoreCase) ? "Music" : entityType;
        return $"{canonicalType}:{entityId}";
    }

    private bool TryGetCurrentMusicGroup(Guid musicPublicId, out string groupName, out Guid clientPublicId)
    {
        groupName = GetGroupName("Music", musicPublicId);
        clientPublicId = default;

        if (!_connectionEntities.TryGetValue(Context.ConnectionId, out var entity))
            return false;
        if (!string.Equals(entity.EntityType, "Music", StringComparison.OrdinalIgnoreCase))
            return false;
        if (entity.EntityId != musicPublicId)
            return false;
        if (entity.ClientPublicId is not Guid id)
            return false;

        clientPublicId = id;
        return true;
    }

    private async Task<MusicListenerIdentity?> GetMusicListenerIdentityAsync(Guid clientPublicId, CancellationToken cancellationToken)
    {
        if (Context.Items.TryGetValue(ListenerIdentityKey, out var cached) && cached is MusicListenerIdentity identity)
            return identity;

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

        var loadedIdentity = new MusicListenerIdentity(client.FirstName, client.LastName, username, profileMediaPublicId);
        Context.Items[ListenerIdentityKey] = loadedIdentity;
        return loadedIdentity;
    }

    private static List<MusicListenerPresenceResponse> GetMusicListeners(string groupName)
    {
        if (!_entityGroups.TryGetValue(groupName, out var group))
            return [];

        List<MusicListenerPresenceResponse> snapshot;
        lock (group.Gate)
        {
            snapshot = new(group.Listeners.Count);
            foreach (var listener in group.Listeners)
                snapshot.Add(ToResponse(listener.Key, listener.Value));
        }

        snapshot.Sort(static (left, right) => left.ClientPublicId.CompareTo(right.ClientPublicId));
        return snapshot;
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

    private sealed class EntityGroup
    {
        public Lock Gate { get; } = new();
        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, MusicListenerState> Listeners { get; } = [];
    }

    private sealed record ConnectionEntity(string EntityType, Guid EntityId, Guid? ClientPublicId);

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
