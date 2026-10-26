namespace Mizon.Server.API.Hubs;

/// <summary>
/// The minimal, client-shaped presence payload for an active music listener.
/// It deliberately contains display fields separately so each client can choose its own presentation.
/// </summary>
public sealed class MusicListenerPresenceResponse
{
    public Guid ClientPublicId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Username { get; init; }
    public Guid? ProfileMediaPublicId { get; init; }
    public long PositionMilliseconds { get; init; }
    public bool IsPlaying { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
