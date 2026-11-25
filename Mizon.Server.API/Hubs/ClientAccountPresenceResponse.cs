namespace Mizon.Server.API.Hubs;

public sealed record ClientAccountPresenceResponse(Guid ClientPublicId, bool IsOnline, long Revision);
