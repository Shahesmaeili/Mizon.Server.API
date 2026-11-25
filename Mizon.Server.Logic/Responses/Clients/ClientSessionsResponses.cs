namespace Mizon.Server.Logic.Responses.Clients;

public sealed record ClientSessionItemResponse(
    Guid SessionPublicId,
    string Platform,
    string AppVersion,
    string OsVersion,
    string DeviceName,
    string DeviceModel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActiveAtUtc,
    bool IsCurrent,
    bool IsOnline,
    string? IpAddress,
    string? Location,
    bool IsUnconfirmed);

public sealed record ClientSessionsResponse(List<ClientSessionItemResponse> Sessions, int InactivityDays) : IApiResponse;
public sealed record TerminateSessionsResponse(int RevokedCount) : IApiResponse;
public sealed record UpdateSessionInactivityResponse(int InactivityDays) : IApiResponse;
public sealed record ConfirmSessionResponse(bool Confirmed) : IApiResponse;
