using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mizon.Database;
using Mizon.Database.Entities;
using Mizon.Server;
using Mizon.Server.API;
using Mizon.Server.API.Endpoints.Clients;
using Mizon.Server.API.Hubs;
using Mizon.Server.API.JsonSerializerContexts;
using Mizon.Server.API.Middlewares;
using Mizon.Server.API.Services;
using Mizon.Server.Logic;
using Mizon.Server.Logic.Operations.Clients;
using Mizon.Server.Logic.Requests.Clients;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class ClientSessionsSqlChecks
{
    internal static async Task RunAsync()
    {
        var name = "MizonSessionFixture_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=localhost;Database={name};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
            .UseSqlServer(connection)
            .Options;
        await using var database = new MizonDatabaseContext(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        try
        {
            if (!await database.Database.EnsureCreatedAsync(token))
                throw new InvalidOperationException("A unique session fixture database was not created.");

            var owner = new Client { PublicId = Guid.NewGuid(), FirstName = "Session", LastName = "Fixture" };
            var unrelated = new Client { PublicId = Guid.NewGuid(), FirstName = "Different", LastName = "Fixture" };
            var platform = new PlatformVersion { PublicId = Guid.NewGuid(), Version = "11.0",
                Platform = new() { PublicId = Guid.NewGuid(), Title = "Windows" } };
            var now = DateTimeOffset.UtcNow;
            var current = NewSession(owner, now, "This device");
            var other = NewSession(owner, now, "Second device");
            other.IsConfirmationPending = true;
            var stale = NewSession(owner, now.AddDays(-20), "Inactive device");
            var foreign = NewSession(unrelated, now, "Foreign device");
            database.ClientSessions.AddRange(current, other, stale, foreign);
            await database.SaveChangesAsync(token);
            // Seed historical times after the write interceptor set the timestamps.
            await database.ClientSessions.Where(row => row.PublicId == current.PublicId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.CreateDateTime, now.AddDays(-2)), token);
            database.ChangeTracker.Clear();

            var credentials = new ClientSessionAuthorization(database);
            var events = new FixtureEvents(other.PublicId);
            var metadata = new RequestMetadata { ClientId = owner.Id, ClientPublicId = owner.PublicId, SessionPublicId = current.PublicId };
            var list = new ClientSessionsOperation(database, events);
            var snapshot = (await list.HandleAsync(new() { Content = new() }, metadata, token)).Content!;
            Check(snapshot.Sessions.Count == 3 && snapshot.Sessions[0].IsCurrent &&
                snapshot.Sessions.Any(row => row.SessionPublicId == other.PublicId && row.IsOnline &&
                    row.IsUnconfirmed && row.IpAddress == "192.0.2.1"),
                "account-only session list, ordering, online state and IP");
            var confirmation = new ConfirmSessionOperation(database, events);
            var confirmed = await confirmation.HandleAsync(new() { Content = new() { SessionPublicId = other.PublicId } }, metadata, token);
            Check(confirmed.Content?.Confirmed == true, "existing device confirms a new sign-in");
            Check(!await credentials.ValidateAsync(owner.PublicId, foreign.PublicId, false, token), "cross-account token is invalid");

            var mutations = new ClientSessionMutations(database, events);
            Check((await mutations.TerminateAsync(metadata, current.PublicId, token)).Error?.Code == 4601, "retain current session");
            Check((await mutations.TerminateAsync(metadata, foreign.PublicId, token)).Error?.Code == 4602, "foreign session is indistinguishable from missing");
            Check((await mutations.TerminateAsync(WithSession(other.PublicId), current.PublicId, token)).Error?.Code == 4603,
                "recent device cannot revoke an older session");

            var policy = new UpdateSessionInactivityOperation(database, events);
            var selected = await policy.HandleAsync(new() { Content = new() { InactivityDays = 7 } }, metadata, token);
            Check(selected.Content?.InactivityDays == 7 && events.Revoked.Contains(stale.PublicId) &&
                !await credentials.ValidateAsync(owner.PublicId, stale.PublicId, false, token), "expired session is revoked");
            await policy.HandleAsync(new() { Content = new() { InactivityDays = 0 } }, metadata, token);
            Check(!await credentials.ValidateAsync(owner.PublicId, stale.PublicId, false, token), "relaxing the policy does not restore a revoked session");

            var all = await mutations.TerminateAsync(metadata, null, token);
            Check(all.Content?.RevokedCount == 1 && events.Revoked.Contains(other.PublicId), "revoke remaining other sessions");
            Check(!await credentials.ValidateAsync(owner.PublicId, other.PublicId, false, token) &&
                await credentials.ValidateAsync(owner.PublicId, current.PublicId, false, token) &&
                await credentials.ValidateAsync(unrelated.PublicId, foreign.PublicId, false, token), "scope of revocation");
            var live = NewSession(owner, now, "WebSocket fixture");
            database.Attach(owner);
            database.Attach(platform);
            database.ClientSessions.Add(live);
            await database.SaveChangesAsync(token);
            await CheckHttpAndSocketAsync(connection, owner.PublicId, current.PublicId, live.PublicId, token);
            Console.WriteLine("PASS SQL device sessions: owned list, current session, signed-in device metadata, recent-session guard, inactivity and revocation.");

            RequestMetadata WithSession(Guid id) => new() { ClientId = owner.Id, ClientPublicId = owner.PublicId, SessionPublicId = id };
            ClientSession NewSession(Client client, DateTimeOffset activity, string label) => new()
            {
                PublicId = Guid.NewGuid(), Client = client, PlatformVersion = platform, LastSeenDateTime = activity,
                OsVersion = "11", DeviceName = label, DeviceIdentifier = label, DeviceModel = "Fixture",
                DeviceUsername = "fixture", RefreshTokenHash = "fixture", ClientSessionIps = [new() { IpAddress = "192.0.2.1" }],
            };
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private static async Task CheckHttpAndSocketAsync(string connection, Guid owner, Guid actor, Guid target, CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Convert.ToBase64String(SHA256.HashData("Mizon session fixture"u8)),
            ["Jwt:Issuer"] = "session-fixture",
            ["Jwt:Audience"] = "session-fixture",
        });
        builder.Services.AddDbContextPool<MizonDatabaseContext>(options => options.UseSqlServer(connection));
        builder.Services.AddMizonServer();
        builder.Services.AddMizonLogic();
        builder.Services.AddScoped<RequestMetadata>();
        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddAuthentication("jwt").AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>("jwt", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<SessionConnections>();
        builder.Services.AddSingleton<IClientSessionEvents>(services => services.GetRequiredService<SessionConnections>());
        builder.Services.AddSignalR(options => options.AddFilter<ActiveSessionHubFilter>())
            .AddJsonProtocol(options => options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default));
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, MizonJsonContexts.Default));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<ClientContextMiddleware>();
        app.MapHub<MizonHub>("/mizonHub");
        new ClientSessions().Map(app);
        await app.StartAsync(token);

        var address = new Uri(app.Urls.Single());
        var jwt = app.Services.GetRequiredService<JwtTokenService>();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + jwt.GenerateAccessToken(owner, target));
        await socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/mizonHub" }.Uri, token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, token);
        var buffer = new byte[4096];
        var first = await socket.ReceiveAsync(buffer, token);
        Check(first.MessageType == WebSocketMessageType.Text, "websocket handshake");
        using (var registrationTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            registrationTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!Encoding.UTF8.GetString(buffer, 0, first.Count).Contains("SessionsChanged", StringComparison.Ordinal))
                first = await socket.ReceiveAsync(buffer, registrationTimeout.Token);
        }

        using var http = new HttpClient { BaseAddress = address };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(owner, actor));
        using var list = await http.GetAsync("/Clients/Sessions", token);
        using var payload = JsonDocument.Parse(await list.Content.ReadAsStreamAsync(token));
        var sessions = payload.RootElement.GetProperty("content").GetProperty("sessions");
        Check(sessions.EnumerateArray().Any(row => row.GetProperty("sessionPublicId").GetGuid() == target &&
            row.GetProperty("isOnline").GetBoolean()), "the live websocket appears online in the real session endpoint");
        using var body = new StringContent($"{{\"content\":{{\"sessionPublicId\":\"{target}\"}}}}", Encoding.UTF8, "application/json");
        using var revoke = await http.PostAsync("/Clients/TerminateSession", body, token);
        using var result = JsonDocument.Parse(await revoke.Content.ReadAsStreamAsync(token));
        Check(result.RootElement.GetProperty("content").GetProperty("revokedCount").GetInt32() == 1, "HTTP revocation");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(owner, target));
        using var unauthorized = await http.GetAsync("/Clients/Sessions", token);
        Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized &&
            unauthorized.Headers.TryGetValues("Mizon-Session-Revoked", out var flags) && flags.Contains("1"),
            "actual endpoint rejects a revoked session with the explicit marker");

        using var closeWait = CancellationTokenSource.CreateLinkedTokenSource(token);
        closeWait.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            WebSocketReceiveResult message;
            do { message = await socket.ReceiveAsync(buffer, closeWait.Token); }
            while (message.MessageType != WebSocketMessageType.Close);
        }
        catch (WebSocketException) { } // Kestrel can abort the transport instead of sending a close frame.
        Check(!app.Services.GetRequiredService<SessionConnections>().IsOnline(target), "revoked websocket is disconnected");
        await app.StopAsync(token);
        Console.WriteLine("PASS device session HTTP/SignalR: signed JWT, live online status, revocation marker and socket close.");
    }

    private static void Check(bool valid, string scenario)
    {
        if (!valid) throw new InvalidOperationException("Session fixture failed: " + scenario);
    }

    private sealed class FixtureEvents(Guid online) : IClientSessionEvents
    {
        internal HashSet<Guid> Revoked { get; } = [];
        public bool IsOnline(Guid sessionId) => sessionId == online;
        public Task UnconfirmedAsync(Guid account) => Task.CompletedTask;
        public Task ChangedAsync(Guid account, IReadOnlyList<Guid> revoked)
        {
            Revoked.UnionWith(revoked);
            return Task.CompletedTask;
        }
    }
}
