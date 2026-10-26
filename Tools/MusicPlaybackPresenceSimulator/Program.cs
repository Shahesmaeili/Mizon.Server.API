using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Mizon.Database;
using Mizon.Server.API.Services;

var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();
var requestedCount = GetBoundedIntArgument(args, "--count", 10, 1, 10);
var requestedMusicPublicId = GetGuidArgument(args, "--music");
var hubUrl = configuration["Simulator:HubUrl"] ?? "https://localhost:5128/mizonHub";
var connectionString = configuration["Simulator:ConnectionString"] ?? "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True";

var options = new DbContextOptionsBuilder<MizonDatabaseContext>().UseSqlServer(connectionString).Options;
await using var database = new MizonDatabaseContext(options);

var musicCandidates = await database.Musics.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.PublicId }).ToListAsync();
if (musicCandidates.Count == 0)
    throw new InvalidOperationException("No music exists in the Mizon database.");

var musicDurations = await database.MusicFiles.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.MusicId, x.Duration }).ToListAsync();
var tracks = musicCandidates.Select(music => new SimulatedTrack(music.PublicId, musicDurations.FirstOrDefault(file => file.MusicId == music.Id)?.Duration ?? TimeSpan.FromMinutes(3))).ToList();
var mainTrack = requestedMusicPublicId is Guid musicPublicId ? tracks.FirstOrDefault(x => x.PublicId == musicPublicId) : tracks[0];
if (mainTrack is null)
    throw new InvalidOperationException("The requested --music public id was not found.");

var clients = (await database.Clients.AsNoTracking().OrderBy(x => x.Id).Select(x => x.PublicId).ToListAsync())
    .OrderBy(_ => Random.Shared.Next())
    .Take(requestedCount)
    .ToList();
if (clients.Count == 0)
    throw new InvalidOperationException("No clients exist in the Mizon database.");

Console.Title = "Mizon Music Playback Presence Simulator";
Console.WriteLine($"Starting {clients.Count} simulated listeners on {mainTrack.PublicId}.");
Console.WriteLine("Actions: play, pause, resume, seek, leave, join another music, and return. Press Ctrl+C to stop.");

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellationSource.Cancel(); };

var jwtTokenService = new JwtTokenService(configuration);
var simulators = clients.Select((clientPublicId, index) => new SimulatedMusicListener(index + 1, clientPublicId, mainTrack, tracks, jwtTokenService, hubUrl)).ToList();

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

internal sealed class SimulatedMusicListener(int number, Guid clientPublicId, SimulatedTrack initialTrack, IReadOnlyList<SimulatedTrack> tracks, JwtTokenService jwtTokenService, string hubUrl) : IAsyncDisposable
{
    private readonly Random _random = new(HashCode.Combine(clientPublicId, number));
    private readonly HubConnection _connection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options => options.AccessTokenProvider = () => Task.FromResult(jwtTokenService.GenerateAccessToken(clientPublicId, Guid.NewGuid())))
        .WithAutomaticReconnect()
        .Build();

    private SimulatedTrack _track = initialTrack;
    private long _positionMilliseconds;
    private bool _isPlaying;
    private bool _isInMusicGroup;
    private DateTimeOffset _nextStateChangeAt;
    private DateTimeOffset _pauseUntil;
    private DateTimeOffset _lastPlaybackAdvanceAt;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await StartWithRetryAsync(cancellationToken);
        await JoinCurrentTrackAsync(cancellationToken);
        _positionMilliseconds = _random.NextInt64(0, Math.Max(1, (long)_track.Duration.TotalMilliseconds));
        _isPlaying = _random.Next(0, 2) == 0;
        _lastPlaybackAdvanceAt = DateTimeOffset.UtcNow;
        ScheduleNextStateChange();
        if (!_isPlaying)
            _pauseUntil = DateTimeOffset.UtcNow.AddSeconds(_random.Next(5, 11));
        await PublishAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_random.Next(250, 900), cancellationToken);
            await ApplyRandomActionAsync(cancellationToken);
        }
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

    private async Task StartWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _connection.StartAsync(cancellationToken);
                return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"listener #{number:00} is waiting for the API...");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private Task PublishAsync(CancellationToken cancellationToken) =>
        _connection.InvokeAsync("UpdateMusicPlayback", _track.PublicId, _positionMilliseconds, _isPlaying, cancellationToken);

    private void Write(string action) => Console.WriteLine($"listener #{number:00} {action}: {_track.PublicId} at {_positionMilliseconds}ms");

    public async ValueTask DisposeAsync()
    {
        try { await _connection.DisposeAsync(); } catch { }
    }
}

internal sealed record SimulatedTrack(Guid PublicId, TimeSpan Duration);
