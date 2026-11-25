namespace Mizon.Server.Logic.Operations.Clients;

public sealed class LogoutOperation(MizonDatabaseContext mizonDatabaseContext, IClientSessionEvents events)
    : IApiOperation<LogoutRequest, LogoutResponse>
{
    public async Task<BaseApiResponse<LogoutResponse>> HandleAsync(
        BaseApiRequest<LogoutRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        if (requestMetadata is not { ClientId: int clientId, ClientPublicId: Guid account, SessionPublicId: Guid sessionId })
            return ClientSessionMutations.Failure<LogoutResponse>(401, "Unauthorized");
        var session = await mizonDatabaseContext.ClientSessions
            .Where(c => c.PublicId == sessionId && c.ClientId == clientId)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            return new()
            {
                Error = new BaseApiError
                {
                    Code = 2001,
                    Title = "Session not found",
                    Details = "The session does not exist."
                }
            };
        }

        session.LogoutDateTime = DateTimeOffset.UtcNow;

        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        await events.ChangedAsync(account, [sessionId]);

        return new BaseApiResponse<LogoutResponse>
        {
            Content = new()
        };
    }
}
