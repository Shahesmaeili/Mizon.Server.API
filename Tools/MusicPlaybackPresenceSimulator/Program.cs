using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Mizon.Database;
using Mizon.Server.API.Hubs;
using Mizon.Server.API.Services;
using System.Net;

var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();
var requestedCount = GetBoundedIntArgument(args, "--count", 10, 1, 10);
var requestedMusicPublicId = GetGuidArgument(args, "--music");
var hubUrl = configuration["Simulator:HubUrl"] ?? "https://localhost:5128/mizonHub";
var connectionString = configuration["Simulator:ConnectionString"] ?? "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True";
var sql = new SqlConnectionStringBuilder(connectionString);
if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var hub) || !hub.IsLoopback || hub.Scheme != Uri.UriSchemeHttps ||
    !hub.AbsolutePath.Equals("/mizonHub", StringComparison.OrdinalIgnoreCase) ||
    hub.UserInfo.Length != 0 || hub.Query.Length != 0 || hub.Fragment.Length != 0 ||
    !sql.IntegratedSecurity || sql.AttachDBFilename.Length != 0 ||
    !new[] { "localhost", "(local)", ".", "127.0.0.1", "::1" }.Contains(sql.DataSource, StringComparer.OrdinalIgnoreCase))
    throw new InvalidOperationException("The simulator requires the local HTTPS API and an integrated-authentication local SQL Server.");

using var instance = TryAcquireInstance();
if (instance is null)
{
    Console.WriteLine("The music presence simulator is already running.");
    return;
}
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellationSource.Cancel(); };
var cancellationToken = cancellationSource.Token;

var options = new DbContextOptionsBuilder<MizonDatabaseContext>().UseSqlServer(connectionString).Options;
await using var database = new MizonDatabaseContext(options);

var musicCandidates = await database.Musics.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PublicId }).ToListAsync(cancellationToken);
if (musicCandidates.Count == 0)
    throw new InvalidOperationException("No music exists in the Mizon database.");

var musicDurations = await database.MusicFiles.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.MusicId, x.Duration }).ToListAsync(cancellationToken);
var tracks = musicCandidates.Select(music => new SimulatedTrack(music.PublicId, musicDurations.FirstOrDefault(file => file.MusicId == music.Id)?.Duration ?? TimeSpan.FromMinutes(3))).ToList();
var mainTrack = requestedMusicPublicId is Guid musicPublicId ? tracks.FirstOrDefault(x => x.PublicId == musicPublicId) : tracks[0];
if (mainTrack is null)
    throw new InvalidOperationException("The requested --music public id was not found.");

Console.Title = "Mizon Music Playback Presence Simulator";
var jwtTokenService = new JwtTokenService(configuration);
var sessionOperation = new SimulatorSessionsOperation(database);
var sessions = await sessionOperation.OpenAsync(requestedCount, cancellationToken);
Console.WriteLine($"Starting {sessions.Length} development fixture listeners on {mainTrack.PublicId}.");
Console.WriteLine("Actions: play, pause, resume, seek, leave, join another music, and return. Press Ctrl+C to stop.");
var simulators = sessions.Select((session, index) =>
    new SimulatedMusicListener(index + 1, session, mainTrack, tracks, jwtTokenService, hubUrl)).ToList();

try
{
    await Task.WhenAll(simulators.Select(listener => listener.RunAsync(cancellationSource.Token)));
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
}
finally
{
    await Task.WhenAll(simulators.Select(listener => listener.DisposeAsync().AsTask()));
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await sessionOperation.CloseAsync(sessions, cleanup.Token);
}

static int GetBoundedIntArgument(string[] args, string name, int defaultValue, int min, int max)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? Math.Clamp(value, min, max) : defaultValue;
}

static Guid? GetGuidArgument(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && Guid.TryParse(args[index + 1], out var value) ? value : null;
}

static FileStream? TryAcquireInstance()
{
    var path = Path.Combine(Path.GetTempPath(), "Mizon.MusicPresenceSimulator.lock");
    try
    {
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
    {
        // Windows reports a sharing or locking violation when the running simulator owns the file.
        return null;
    }
}

internal sealed class SimulatedMusicListener(int number, SimulatorSession session, SimulatedTrack initialTrack,
    IReadOnlyList<SimulatedTrack> tracks, JwtTokenService jwtTokenService, string hubUrl) : IAsyncDisposable
{
    private readonly Random _random = new(HashCode.Combine(session.ClientPublicId, number));
    private readonly HubConnection _connection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options => options.AccessTokenProvider = () =>
            Task.FromResult<string?>(jwtTokenService.GenerateAccessToken(session.ClientPublicId, session.SessionPublicId)))
        .WithAutomaticReconnect(new SimulatorReconnectPolicy())
        .Build();

    private SimulatedTrack _track = initialTrack;
    private long _positionMilliseconds;
    private bool _isPlaying;
    private bool _isInMusicGroup;
    private DateTimeOffset _nextStateChangeAt;
    private DateTimeOffset _pauseUntil;
    private DateTimeOffset _lastPlaybackAdvanceAt;
    private int _needsRejoin;
    private int _sessionRevoked;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // The hub broadcasts these events to the simulator even though it only publishes playback.
        using var onlineCount = _connection.On<OnlineClientCount>("OnlineClientCountChanged", static _ => { });
        using var viewers = _connection.On<string, Guid, int>("EntityViewersCountUpdated", static (_, _, _) => { });
        using var snapshot = _connection.On<Guid, List<MusicListenerPresenceResponse>>("MusicListenersSnapshot", static (_, _) => { });
        using var updated = _connection.On<Guid, MusicListenerPresenceResponse>("MusicListenerUpdated", static (_, _) => { });
        using var removed = _connection.On<Guid, Guid>("MusicListenerRemoved", static (_, _) => { });
        using var sessionsChanged = _connection.On("SessionsChanged", static () => { });
        using var revoked = _connection.On("SessionRevoked", () => Interlocked.Exchange(ref _sessionRevoked, 1));
        _connection.Reconnected += ReconnectedAsync;

        if (!await StartWithRetryAsync(cancellationToken)) return;
        _positionMilliseconds = _random.NextInt64(0, Math.Max(1, (long)_track.Duration.TotalMilliseconds));
        _isPlaying = _random.Next(0, 2) == 0;
        _lastPlaybackAdvanceAt = DateTimeOffset.UtcNow;
        ScheduleNextStateChange();
        if (!_isPlaying)
            _pauseUntil = DateTimeOffset.UtcNow.AddSeconds(_random.Next(5, 11));
        await JoinCurrentTrackAsync(cancellationToken);
        await PublishAsync(cancellationToken);
        Console.WriteLine($"Listener #{number:00} connected and published playback.");

        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _sessionRevoked) == 0)
        {
            await Task.Delay(_random.Next(250, 900), cancellationToken);
            if (Volatile.Read(ref _sessionRevoked) != 0 || _connection.State == HubConnectionState.Disconnected)
                return;
            if (_connection.State != HubConnectionState.Connected) continue;
            try
            {
                if (Interlocked.Exchange(ref _needsRejoin, 0) != 0 && _isInMusicGroup)
                    await JoinCurrentTrackAsync(cancellationToken);
                await ApplyRandomActionAsync(cancellationToken);
            }
            catch (Exception error) when ((error is InvalidOperationException or IOException) &&
                _connection.State != HubConnectionState.Connected)
            {
                // Reconnection restores group membership before publication resumes.
            }
        }
    }

    private Task ReconnectedAsync(string? connectionId)
    {
        Interlocked.Exchange(ref _needsRejoin, 1);
        return Task.CompletedTask;
    }

    private async Task ApplyRandomActionAsync(CancellationToken cancellationToken)
    {
        if (!_isInMusicGroup)
        {
            _track = _random.Next(100) < 75 ? tracks[0] : tracks[_random.Next(tracks.Count)];
            _positionMilliseconds = _random.NextInt64(0, Math.Max(1, (long)_track.Duration.TotalMilliseconds));
            _isPlaying = _random.Next(0, 2) == 0;
            _lastPlaybackAdvanceAt = DateTimeOffset.UtcNow;
            await JoinCurrentTrackAsync(cancellationToken);
            ScheduleNextStateChange();
            if (!_isPlaying)
                _pauseUntil = DateTimeOffset.UtcNow.AddSeconds(_random.Next(5, 11));
            await PublishAsync(cancellationToken);
            Write("joined");
            return;
        }

        if (!_isPlaying)
        {
            if (DateTimeOffset.UtcNow < _pauseUntil)
            {
                await PublishAsync(cancellationToken);
                return;
            }

            _isPlaying = true;
            _lastPlaybackAdvanceAt = DateTimeOffset.UtcNow;
            ScheduleNextStateChange();
            await PublishAsync(cancellationToken);
            Write("resumed");
            return;
        }

        if (DateTimeOffset.UtcNow < _nextStateChangeAt)
        {
            AdvancePlaybackPosition();
            await PublishAsync(cancellationToken);
            return;
        }

        var action = _random.Next(100);
        if (action < 24)
        {
            AdvancePlaybackPosition();
            _isPlaying = false;
            _pauseUntil = DateTimeOffset.UtcNow.AddSeconds(_random.Next(5, 11));
            await PublishAsync(cancellationToken);
            Write("paused");
            return;
        }

        if (action < 58)
        {
            _positionMilliseconds = _random.NextInt64(0, Math.Max(1, (long)_track.Duration.TotalMilliseconds));
            ScheduleNextStateChange();
            await PublishAsync(cancellationToken);
            Write("seeked");
            return;
        }

        if (action < 82 && tracks.Count > 1)
        {
            _track = tracks[_random.Next(tracks.Count)];
            _positionMilliseconds = _random.NextInt64(0, Math.Max(1, (long)_track.Duration.TotalMilliseconds));
            _isPlaying = _random.Next(0, 2) == 0;
            _lastPlaybackAdvanceAt = DateTimeOffset.UtcNow;
            await JoinCurrentTrackAsync(cancellationToken);
            ScheduleNextStateChange();
            if (!_isPlaying)
                _pauseUntil = DateTimeOffset.UtcNow.AddSeconds(_random.Next(5, 11));
            await PublishAsync(cancellationToken);
            Write("switched music");
            return;
        }

        if (action < 87)
        {
            await _connection.InvokeAsync("LeaveEntity", cancellationToken);
            _isInMusicGroup = false;
            Write("left");
            return;
        }

        AdvancePlaybackPosition();
        await PublishAsync(cancellationToken);
    }

    private void ScheduleNextStateChange() => _nextStateChangeAt = DateTimeOffset.UtcNow.AddSeconds(_random.Next(15, 31));

    private void AdvancePlaybackPosition()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsedMilliseconds = Math.Max(0, (long)(now - _lastPlaybackAdvanceAt).TotalMilliseconds);
        _lastPlaybackAdvanceAt = now;
        _positionMilliseconds = Math.Min((long)_track.Duration.TotalMilliseconds, _positionMilliseconds + elapsedMilliseconds);
        if (_positionMilliseconds >= (long)_track.Duration.TotalMilliseconds)
            _isPlaying = false;
    }

    private async Task JoinCurrentTrackAsync(CancellationToken cancellationToken)
    {
        await _connection.InvokeAsync("JoinEntity", "Music", _track.PublicId, cancellationToken);
        _isInMusicGroup = true;
    }

    private async Task<bool> StartWithRetryAsync(CancellationToken cancellationToken)
    {
        var retryDelay = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _connection.StartAsync(cancellationToken);
                return true;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                if (SimulatorReconnectPolicy.RejectedStatus(error) is { } status)
                {
                    Console.WriteLine($"Listener #{number:00} stopped after HTTP {(int)status}. Check the local API and fixture session.");
                    return false;
                }
                if (error is not (HttpRequestException or IOException or TimeoutException or OperationCanceledException))
                    throw;
                Console.WriteLine($"Listener #{number:00} is waiting for the API. Retry in {retryDelay}s.");
                await Task.Delay(TimeSpan.FromSeconds(retryDelay), cancellationToken);
                retryDelay = Math.Min(retryDelay * 2, 30);
            }
        }
        return false;
    }

    private Task PublishAsync(CancellationToken cancellationToken) =>
        _connection.InvokeAsync("UpdateMusicPlayback", _track.PublicId, _positionMilliseconds, _isPlaying, cancellationToken);

    private void Write(string action) => Console.WriteLine($"listener #{number:00} {action}: {_track.PublicId} at {_positionMilliseconds}ms");

    public async ValueTask DisposeAsync()
    {
        _connection.Reconnected -= ReconnectedAsync;
        try { await _connection.DisposeAsync(); } catch { }
    }
}

internal sealed record SimulatedTrack(Guid PublicId, TimeSpan Duration);

internal sealed class SimulatorReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public TimeSpan? NextRetryDelay(RetryContext context) =>
        RejectedStatus(context.RetryReason) is not null || context.PreviousRetryCount >= Delays.Length
            ? null : Delays[context.PreviousRetryCount];

    internal static HttpStatusCode? RejectedStatus(Exception? error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } status } &&
                (int)status is >= 400 and < 500 && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
                return status;
        }
        return null;
    }
}
