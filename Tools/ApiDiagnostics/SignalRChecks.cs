using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Mizon.Server.API.Hubs;

internal static class SignalRChecks
{
    public static async Task RunAsync()
    {
        var client = Guid.NewGuid();
        using var first = new Fixture(client);
        using var second = new Fixture(client);
        await first.Hub.RegisterDevice("fixture-token");
        await second.Hub.RegisterDevice("fixture-token");
        await first.Hub.UnregisterDevice();
        await second.Hub.UnregisterDevice();
        Require(first.Messages.Any(message => message.Method == "UnregistrationConfirmed"), "first device can unregister");
        Require(second.Messages.Any(message => message.Method == "UnregistrationConfirmed"), "same user's second device unregisters independently");
        await ExpectHubError(() => first.Hub.RegisterDevice(string.Empty));

        var presence = await first.Hub.SetWindowPresence(true, 2);
        Require(presence.ClientPublicId == client && presence.ConnectionId == first.Context.ConnectionId
            && presence.IsOnline && presence.Revision == 2, "presence identity is authenticated and connection-scoped");
        var stale = await first.Hub.SetWindowPresence(false, 1);
        Require(ReferenceEquals(presence, stale), "stale presence cannot overwrite the newest revision");
        Require(first.Messages.Count(message => message.Method == "WindowPresenceChanged") == 1
            && first.Messages.Last().Target == "caller", "presence is sent only to its owner, once per new revision");
        await second.Hub.SetWindowPresence(false, 1);
        Require((await first.Hub.SetWindowPresence(false, 1)).IsOnline, "another connection has independent focus");
        await ExpectHubError(() => first.Hub.SetWindowPresence(true, 0));
        using (var invalidIdentity = new Fixture(Guid.Empty))
            await ExpectHubError(() => invalidIdentity.Hub.SetWindowPresence(true, 1));
        using (var canceledPresence = new Fixture(Guid.NewGuid()))
        {
            canceledPresence.Context.Abort();
            try { await canceledPresence.Hub.SetWindowPresence(true, 1); throw new InvalidOperationException("Expected canceled presence."); }
            catch (OperationCanceledException) { }
            Require(canceledPresence.Context.Items.Count == 0, "canceled presence stores no connection state");
        }
        using (var concurrentPresence = new Fixture(Guid.NewGuid()))
        {
            await Task.WhenAll(Enumerable.Range(1, 100).Select(revision => Task.Run(() =>
                concurrentPresence.Hub.SetWindowPresence(revision % 2 == 0, revision))));
            Require((await concurrentPresence.Hub.SetWindowPresence(false, 1)).Revision == 100,
                "concurrent presence mutations retain the highest revision");
            await concurrentPresence.Hub.OnDisconnectedAsync(null);
            Require(concurrentPresence.Context.Items.Count == 0, "concurrent presence has bounded per-connection cleanup");
        }

        var music = Guid.NewGuid();
        var group = $"Music:{music}";
        await first.Hub.JoinEntity("music", music);
        await second.Hub.JoinEntity("Music", music);
        Require(first.Groups.Contains(first.Context.ConnectionId, group), "Music group name is canonical");
        Require(second.LastCount(music) == 2, "viewer count includes both case variants");
        var snapshot = second.Messages.Last(message => message.Method == "MusicListenersSnapshot");
        Require((Guid)snapshot.Arguments[0]! == music, "snapshot music argument preserved");
        Require(snapshot.Arguments[1] is List<MusicListenerPresenceResponse> { Count: 0 }, "snapshot payload type preserved");
        await first.Hub.LeaveEntity();
        await first.Hub.OnDisconnectedAsync(null);
        Require(first.Context.Items.Count == 0, "presence is removed on disconnect without global state");
        Require(first.LastCount(music) == 1, "leave decrements viewer count");
        Require(!first.Groups.Contains(first.Context.ConnectionId, group), "leave removes transport membership");
        await first.Hub.LeaveEntity(); // A repeated leave must not change a remaining user's count.
        await second.Hub.OnDisconnectedAsync(null);
        Require(second.LastCount(music) == 0, "disconnect removes final viewer");
        Require(second.Context.Items.Count == 0, "connection-owned registration is released");
        await first.Hub.JoinEntity("Music", music);
        Require(first.LastCount(music) == 1, "rejoining an emptied group starts at one");
        await first.Hub.LeaveEntity();
        Console.WriteLine("PASS SignalR independent registration, canonical groups, viewer/snapshot contract, leave/disconnect/rejoin.");

        using var failing = new Fixture(Guid.NewGuid());
        var failedMusic = Guid.NewGuid();
        failing.FailNextSend = true;
        await ExpectHubError(() => failing.Hub.JoinEntity("Music", failedMusic));
        Require(!failing.Groups.Contains(failing.Context.ConnectionId, $"Music:{failedMusic}"), "failed join cleans transport membership");
        using var afterFailure = new Fixture(Guid.NewGuid());
        await afterFailure.Hub.JoinEntity("Music", failedMusic);
        Require(afterFailure.LastCount(failedMusic) == 1, "failed send does not retain a ghost viewer");
        afterFailure.FailNextSend = true;
        await ExpectHubError(afterFailure.Hub.LeaveEntity);
        await failing.Hub.JoinEntity("Music", failedMusic);
        Require(failing.LastCount(failedMusic) == 1, "leave releases state even if broadcast fails");
        await failing.Hub.LeaveEntity();

        using var disconnected = new Fixture(Guid.NewGuid());
        await disconnected.Hub.RegisterDevice("fixture-token");
        await disconnected.Hub.JoinEntity("Music", failedMusic);
        disconnected.FailNextSend = true;
        await disconnected.Hub.OnDisconnectedAsync(new IOException("simulated transport disconnect"));
        Require(disconnected.Context.Items.Count == 0, "disconnect releases owned state despite send failure");
        Console.WriteLine("PASS SignalR state cleanup after join, leave, and disconnect transport failures.");

        // Join/leave on different connections concurrently, including removal and recreation of the last group.
        var contestedMusic = Guid.NewGuid();
        var participants = Enumerable.Range(0, 32).Select(_ => new Fixture(Guid.NewGuid())).ToArray();
        try
        {
            await Task.WhenAll(participants.Select(participant => Task.Run(async () =>
            {
                for (var iteration = 0; iteration < 20; iteration++)
                {
                    await participant.Hub.JoinEntity("Music", contestedMusic);
                    if ((iteration & 1) == 0)
                        await Task.Yield();
                    await participant.Hub.LeaveEntity();
                }
            })));

            using var observer = new Fixture(Guid.NewGuid());
            await observer.Hub.JoinEntity("Music", contestedMusic);
            Require(observer.LastCount(contestedMusic) == 1, "concurrent joins/leaves retain no ghost viewers");
            await observer.Hub.LeaveEntity();
            Require(participants.All(participant => participant.Groups.Count == 0), "all transport memberships released");
            Require(participants.SelectMany(participant => participant.Messages)
                .Where(message => message.Method == "EntityViewersCountUpdated")
                .All(message => (int)message.Arguments[2]! is >= 0 and <= 32), "counts remain bounded under concurrency");
        }
        finally
        {
            foreach (var participant in participants)
            {
                await participant.Hub.OnDisconnectedAsync(null);
                participant.Dispose();
            }
        }

        // These guards must return before database lookup (the fixture deliberately has no DbContext).
        using var guard = new Fixture(Guid.NewGuid());
        await guard.Hub.UpdateMusicPlayback(Guid.NewGuid(), 1000, true);
        await guard.Hub.JoinEntity("Music", music);
        await guard.Hub.UpdateMusicPlayback(Guid.NewGuid(), 2000, false);
        await guard.Hub.LeaveEntity();
        Require(!guard.Messages.Any(message => message.Method == "MusicListenerUpdated"), "off-page playback updates are ignored");
        Console.WriteLine("PASS SignalR 640 concurrent join/leave cycles, group recreation, and playback membership guards.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("SignalR regression: " + message);
    }

    private static async Task ExpectHubError(Func<Task> action)
    {
        try { await action(); }
        catch (HubException) { return; }
        throw new InvalidOperationException("Expected HubException was not raised.");
    }

    private sealed record SentMessage(string Target, string Method, object?[] Arguments);

    private sealed class Fixture : IDisposable
    {
        private int _failNextSend;
        public bool FailNextSend { set => Interlocked.Exchange(ref _failNextSend, value ? 1 : 0); }
        public TestContext Context { get; }
        public TestGroups Groups { get; } = new();
        public ConcurrentQueue<SentMessage> Messages { get; } = new();
        public MizonHub Hub { get; }

        public Fixture(Guid clientId)
        {
            Context = new(clientId);
            Hub = new(NullLogger<MizonHub>.Instance, null!)
            {
                Context = Context,
                Clients = new TestClients(this),
                Groups = Groups
            };
        }

        public int LastCount(Guid music) => (int)Messages.Last(message =>
            message.Method == "EntityViewersCountUpdated" && (Guid)message.Arguments[1]! == music).Arguments[2]!;

        public Task SendAsync(string target, string method, object?[] args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _failNextSend, 0) == 1)
                throw new IOException("simulated transport write failure");
            Messages.Enqueue(new(target, method, args));
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Hub.Dispose();
            Context.Dispose();
        }
    }

    private sealed class TestContext : HubCallerContext, IDisposable
    {
        private readonly CancellationTokenSource _aborted = new();
        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public override string? UserIdentifier { get; }
        public override ClaimsPrincipal User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => _aborted.Token;
        public TestContext(Guid clientId)
        {
            UserIdentifier = clientId.ToString();
            User = new(new ClaimsIdentity([new Claim("sub", UserIdentifier)], "fixture"));
        }
        public override void Abort() => _aborted.Cancel();
        public void Dispose() => _aborted.Dispose();
    }

    private sealed class TestGroups : IGroupManager
    {
        private readonly ConcurrentDictionary<(string Connection, string Group), byte> _members = new();
        public int Count => _members.Count;
        public bool Contains(string connection, string group) => _members.ContainsKey((connection, group));
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _members.TryAdd((connectionId, groupName), 0);
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            _members.TryRemove((connectionId, groupName), out _);
            return Task.CompletedTask;
        }
    }

    private sealed class TestProxy(Fixture owner, string target) : ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            owner.SendAsync(target, method, args, cancellationToken);
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The hub does not invoke client-result methods.");
    }

    private sealed class TestClients(Fixture owner) : IHubCallerClients
    {
        public IClientProxy All => new TestProxy(owner, "all");
        public IClientProxy Others => new TestProxy(owner, "others");
        public ISingleClientProxy Caller => new TestProxy(owner, "caller");
        IClientProxy IHubCallerClients<IClientProxy>.Caller => Caller;
        public ISingleClientProxy Client(string connectionId) => new TestProxy(owner, connectionId);
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
        public IClientProxy Group(string groupName) => new TestProxy(owner, groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy OthersInGroup(string groupName) => Group(groupName);
        public IClientProxy User(string userId) => new TestProxy(owner, userId);
        public IClientProxy Users(IReadOnlyList<string> userIds) => All;
    }
}
