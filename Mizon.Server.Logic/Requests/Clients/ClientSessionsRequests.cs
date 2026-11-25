namespace Mizon.Server.Logic.Requests.Clients;

public sealed class ClientSessionsRequest : IApiRequest;
public sealed class TerminateOtherSessionsRequest : IApiRequest;

public sealed class ConfirmSessionRequest : IApiRequest
{
    public Guid SessionPublicId { get; init; }
}

public sealed class TerminateSessionRequest : IApiRequest
{
    public Guid SessionPublicId { get; init; }
}

public sealed class UpdateSessionInactivityRequest : IApiRequest
{
    public int InactivityDays { get; init; }
}
