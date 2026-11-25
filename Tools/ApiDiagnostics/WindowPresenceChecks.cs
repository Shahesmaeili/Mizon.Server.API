using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mizon.Server.API.Hubs;
using Mizon.Server.API.JsonSerializerContexts;

internal static class WindowPresenceChecks
{
    private static readonly MizonJsonContexts Json = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static async Task RunAsync(bool useDemo = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("presence-fixture")
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("presence-fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.TypeInfoResolver = MizonJsonContexts.Default);
        // Presence must not touch EF; the production hub receives no database in this fixture.
        if (useDemo)
        {
            builder.Services.AddSingleton<TaskbarAudienceDemo>();
            builder.Services.AddHostedService(provider => provider.GetRequiredService<TaskbarAudienceDemo>());
        }
        builder.Services.AddTransient(provider => new MizonHub(provider.GetRequiredService<ILogger<MizonHub>>(), null!,
            provider.GetService<TaskbarAudienceDemo>()));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<MizonHub>("/presence");
        await app.StartAsync(token);
        var address = new Uri(app.Urls.Single());
        try
        {
            using var http = new HttpClient();
            using var denied = await http.PostAsync(new Uri(address, "/presence/negotiate?negotiateVersion=1"), null, token);
            Ensure(denied.StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated connections are rejected by the real hub endpoint.");

            if (useDemo)
            {
                await CheckDemoAsync(address, token);
                return;
            }

            var client = Guid.NewGuid();
            await using var first = await Peer.ConnectAsync(address, client, token);
            await using var second = await Peer.ConnectAsync(address, client, token);
            var singleAccount = await first.GetOnlineCountAsync(token);
            Ensure(singleAccount.Count == 1, "Multiple authenticated connections count as one account.");
            var otherClient = Guid.NewGuid();
            await using (var visitor = await Peer.ConnectAsync(address, otherClient, token))
            {
                var twoAccounts = await visitor.GetOnlineCountAsync(token);
                Ensure(twoAccounts.Count == 2 && twoAccounts.Revision > singleAccount.Revision,
                    "The aggregate updates and increments its revision for a new unique account.");
            }
            for (var attempt = 0; attempt < 20 && (await first.GetOnlineCountAsync(token)).Count != 1; attempt++)
                await Task.Delay(25, token);
            Ensure((await first.GetOnlineCountAsync(token)).Count == 1, "Disconnect clears the last connection for an account.");
            Ensure(await first.GetOnlineFollowerCountAsync(token) == 0, "An account is excluded from its own online followers.");
            var online = await first.SetAsync(true, 3, token);
            Ensure(online.ClientPublicId == client && online.IsOnline && online.Revision == 3
                && !string.IsNullOrEmpty(online.ConnectionId), "Production SignalR serializes the authenticated DTO and caller event.");
            Ensure(first.Events.Count == 1 && first.Events[0].ConnectionId == online.ConnectionId, "Caller receives matching event and invocation result.");
            var stale = await first.SetAsync(false, 2, token);
            Ensure(stale.IsOnline && stale.Revision == 3 && first.Events.Count == 1, "Stale transport invocation is ignored, without another event.");
            var other = await second.SetAsync(false, 1, token);
            Ensure(!other.IsOnline && other.ConnectionId != online.ConnectionId && second.Events.Count == 1,
                "The same account's second connection receives only its own presence event.");
            var offline = await first.SetAsync(false, 4, token);
            Ensure(!offline.IsOnline && offline.Revision == 4 && first.Events.Count == 2,
                "No other connection's event was broadcast to the first peer.");
            await first.ExpectErrorAsync(true, 0, token);
            await using var invalid = await Peer.ConnectAsync(address, Guid.Empty, token);
            await invalid.ExpectErrorAsync(true, 1, token);
            await invalid.ExpectWatchErrorAsync("", token);

            await first.DisposeAsync();
            await using var reconnected = await Peer.ConnectAsync(address, client, token);
            var fresh = await reconnected.SetAsync(false, 1, token);
            Ensure(!fresh.IsOnline && fresh.Revision == 1 && fresh.ConnectionId != online.ConnectionId,
                "A replacement connection starts independently; old revisions/state cannot leak into it.");
            Console.WriteLine("PASS production SignalR window presence: generated JSON, real authenticated WebSockets, own-caller isolation, stale revisions, reconnect, no database.");

            await CheckAccountPresenceAsync(address, client, otherClient, reconnected, second, token);
        }
        finally { await app.StopAsync(token); }
    }

    private static async Task CheckAccountPresenceAsync(Uri address, Guid account, Guid otherAccount,
        Peer desktop, Peer background, CancellationToken token)
    {
        await using var other = await Peer.ConnectAsync(address, otherAccount, token);
        var otherSnapshot = await other.WatchAsync(token);
        var initial = await background.WatchAsync(token);
        await desktop.WatchAsync(token);
        Ensure(initial.ClientPublicId == account && !initial.IsOnline, "An inactive watcher does not make its account online.");
        Ensure(otherSnapshot.ClientPublicId == otherAccount && !otherSnapshot.IsOnline, "Each watcher can read only its authenticated account.");
        await other.ExpectWatchErrorAsync($"\"{account:D}\"", token);

        var active = await desktop.SetAsync(true, 2, token);
        Ensure(active.IsOnline, "The legacy sender still receives a local active acknowledgement.");
        var accountOnline = await background.NextAccountPresenceAsync(token);
        Ensure(accountOnline.ClientPublicId == account && accountOnline.IsOnline && accountOnline.Revision > initial.Revision,
            "An inactive device receives the confirmed online state of its own account.");
        var inactive = await background.SetAsync(false, 2, token);
        Ensure(!inactive.IsOnline && (await background.WatchAsync(token)) == accountOnline,
            "Local inactivity is acknowledged without overriding the account's active device.");

        await background.SetAsync(true, 3, token);
        await desktop.SetAsync(false, 3, token);
        Ensure((await desktop.WatchAsync(token)) == accountOnline
            && desktop.AccountEvents.Count == 1 && background.AccountEvents.Count == 1,
            "Separately active devices keep the aggregate online without duplicate state-change events.");

        await background.DisposeAsync();
        var accountOffline = await desktop.NextAccountPresenceAsync(token);
        Ensure(accountOffline.ClientPublicId == account && !accountOffline.IsOnline
            && accountOffline.Revision > accountOnline.Revision,
            "Disconnecting the last active device broadcasts offline to the remaining watcher.");
        var isolated = await other.WatchAsync(token);
        Ensure(isolated.ClientPublicId == otherAccount && !isolated.IsOnline && other.AccountEvents.Count == 0,
            "No account presence update reaches a different identity.");

        await using var fresh = await Peer.ConnectAsync(address, account, token);
        var snapshot = await fresh.WatchAsync(token);
        Ensure(!snapshot.IsOnline && snapshot.ClientPublicId == account && snapshot.Revision >= accountOffline.Revision,
            "Reconnect reads the remaining state on a fresh authenticated subscription.");
        Console.WriteLine("PASS production SignalR account presence: inactive same-account watcher, independent devices, disconnect, reconnect, and cross-account isolation.");
    }

    private static async Task CheckDemoAsync(Uri address, CancellationToken token)
    {
        await using var peer = await Peer.ConnectAsync(address, Guid.NewGuid(), token);
        var previous = await peer.GetOnlineCountAsync(token);
        var previousFollowers = await peer.GetOnlineFollowerCountAsync(token);
        Ensure(previous.Count is >= 18 and <= 65 && previousFollowers is >= 1 and <= 12,
            "The first authenticated snapshot contains both demo counts.");

        for (var update = 0; update < 2; update++)
        {
            var current = await peer.GetNextOnlineCountAsync(token);
            var followers = await peer.GetOnlineFollowerCountAsync(token);
            Ensure(current is { Count: >= 18 and <= 65 } && current.Revision > previous.Revision && current.Count != previous.Count,
                "The normal SignalR event delivers a new in-range demo total and newer revision.");
            Ensure(followers is >= 1 and <= 12 && followers != previousFollowers && followers < current.Count,
                "The follower request sees its updated demo counterpart without needing EF.");
            Console.WriteLine($"PASS demo SignalR audience revision {current.Revision}: {current.Count} accounts, {followers} followers.");
            previous = current;
            previousFollowers = followers;
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Presence-Fixture", out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class Peer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly StringBuilder _pending = new();
        private readonly byte[] _buffer = new byte[4096];
        private int _invocation;
        public List<ClientWindowPresenceResponse> Events { get; } = [];
        public List<ClientAccountPresenceResponse> AccountEvents { get; } = [];

        public static async Task<Peer> ConnectAsync(Uri address, Guid client, CancellationToken token)
        {
            var peer = new Peer();
            try
            {
                peer._socket.Options.SetRequestHeader("X-Presence-Fixture", client.ToString());
                await peer._socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/presence" }.Uri, token);
                await peer.SendAsync("{\"protocol\":\"json\",\"version\":1}", token);
                var handshake = await peer.ReadAsync(token);
                Ensure(!handshake.TryGetProperty("error", out _), "SignalR handshake accepted.");
                return peer;
            }
            catch { await peer.DisposeAsync(); throw; }
        }

        public async Task<ClientWindowPresenceResponse> SetAsync(bool active, long revision, CancellationToken token)
        {
            var response = await InvokeAsync(active, revision, token);
            Ensure(!response.TryGetProperty("error", out _), "Presence invocation succeeded.");
            return response.GetProperty("result").Deserialize(Json.ClientWindowPresenceResponse)!;
        }

        public async Task ExpectErrorAsync(bool active, long revision, CancellationToken token)
        {
            Ensure((await InvokeAsync(active, revision, token)).TryGetProperty("error", out _), "Invalid presence rejected.");
        }

        public async Task<ClientAccountPresenceResponse> WatchAsync(CancellationToken token) =>
            (await InvokeAsync("WatchAccountPresence", "", token)).GetProperty("result").Deserialize(Json.ClientAccountPresenceResponse)!;

        public async Task ExpectWatchErrorAsync(string arguments, CancellationToken token) =>
            Ensure((await InvokeAsync("WatchAccountPresence", arguments, token)).TryGetProperty("error", out _),
                "A watcher without a valid authenticated account or with a caller-selected target is rejected.");

        public async Task<ClientAccountPresenceResponse> NextAccountPresenceAsync(CancellationToken token)
        {
            while (true)
            {
                if (RecordAccountPresence(await ReadAsync(token)) is { } response)
                    return response;
            }
        }

        public async Task<OnlineClientCount> GetOnlineCountAsync(CancellationToken token) =>
            (await InvokeAsync("GetOnlineClientCount", "", token)).GetProperty("result").Deserialize(Json.OnlineClientCount)!;

        public async Task<int> GetOnlineFollowerCountAsync(CancellationToken token) =>
            (await InvokeAsync("GetOnlineFollowerCount", "", token)).GetProperty("result").GetInt32();

        public async Task<OnlineClientCount> GetNextOnlineCountAsync(CancellationToken token)
        {
            while (true)
            {
                var message = await ReadAsync(token);
                if (message.TryGetProperty("target", out var target) && target.GetString() == "OnlineClientCountChanged")
                    return message.GetProperty("arguments")[0].Deserialize(Json.OnlineClientCount)!;
            }
        }

        private Task<JsonElement> InvokeAsync(bool active, long revision, CancellationToken token) =>
            InvokeAsync("SetWindowPresence", $"{(active ? "true" : "false")},{revision}", token);

        private async Task<JsonElement> InvokeAsync(string method, string arguments, CancellationToken token)
        {
            var id = (++_invocation).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendAsync($"{{\"type\":1,\"invocationId\":\"{id}\",\"target\":\"{method}\",\"arguments\":[{arguments}]}}", token);
            while (true)
            {
                var message = await ReadAsync(token);
                if (message.TryGetProperty("target", out var target) && target.GetString() == "WindowPresenceChanged")
                    Events.Add(message.GetProperty("arguments")[0].Deserialize(Json.ClientWindowPresenceResponse)!);
                RecordAccountPresence(message);
                if (message.TryGetProperty("invocationId", out var invocation) && invocation.GetString() == id)
                    return message;
            }
        }

        private ClientAccountPresenceResponse? RecordAccountPresence(JsonElement message)
        {
            if (!message.TryGetProperty("target", out var target) || target.GetString() != "AccountPresenceChanged")
                return null;
            var response = message.GetProperty("arguments")[0].Deserialize(Json.ClientAccountPresenceResponse)!;
            AccountEvents.Add(response);
            return response;
        }

        private Task SendAsync(string json, CancellationToken token) =>
            _socket.SendAsync(Encoding.UTF8.GetBytes(json + '\u001e'), WebSocketMessageType.Text, true, token);

        private async Task<JsonElement> ReadAsync(CancellationToken token)
        {
            while (true)
            {
                var separator = _pending.ToString().IndexOf('\u001e');
                if (separator >= 0)
                {
                    var message = _pending.ToString(0, separator);
                    _pending.Remove(0, separator + 1);
                    using var document = JsonDocument.Parse(message);
                    return document.RootElement.Clone();
                }
                var result = await _socket.ReceiveAsync(_buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Unexpected test connection close.");
                _pending.Append(Encoding.UTF8.GetString(_buffer, 0, result.Count));
            }
        }

        public ValueTask DisposeAsync() { _socket.Dispose(); return ValueTask.CompletedTask; }
    }
}
