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

    public static async Task RunAsync()
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
        builder.Services.AddTransient(provider => new MizonHub(provider.GetRequiredService<ILogger<MizonHub>>(), null!));
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

            var client = Guid.NewGuid();
            await using var first = await Peer.ConnectAsync(address, client, token);
            await using var second = await Peer.ConnectAsync(address, client, token);
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

            await first.DisposeAsync();
            await using var reconnected = await Peer.ConnectAsync(address, client, token);
            var fresh = await reconnected.SetAsync(false, 1, token);
            Ensure(!fresh.IsOnline && fresh.Revision == 1 && fresh.ConnectionId != online.ConnectionId,
                "A replacement connection starts independently; old revisions/state cannot leak into it.");
            Console.WriteLine("PASS production SignalR window presence: generated JSON, real authenticated WebSockets, own-caller isolation, stale revisions, reconnect, no database.");
        }
        finally { await app.StopAsync(token); }
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

        private async Task<JsonElement> InvokeAsync(bool active, long revision, CancellationToken token)
        {
            var id = (++_invocation).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await SendAsync($"{{\"type\":1,\"invocationId\":\"{id}\",\"target\":\"SetWindowPresence\",\"arguments\":[{(active ? "true" : "false")},{revision}]}}", token);
            while (true)
            {
                var message = await ReadAsync(token);
                if (message.TryGetProperty("target", out var target) && target.GetString() == "WindowPresenceChanged")
                    Events.Add(message.GetProperty("arguments")[0].Deserialize(Json.ClientWindowPresenceResponse)!);
                if (message.TryGetProperty("invocationId", out var invocation) && invocation.GetString() == id)
                    return message;
            }
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
