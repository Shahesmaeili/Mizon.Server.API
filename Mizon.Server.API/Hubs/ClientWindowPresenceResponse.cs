namespace Mizon.Server.API.Hubs;

public sealed class ClientWindowPresenceResponse
{
    public Guid ClientPublicId { get; init; }
    public string ConnectionId { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public long Revision { get; init; }
}
