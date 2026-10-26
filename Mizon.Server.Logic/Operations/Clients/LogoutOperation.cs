namespace Mizon.Server.Logic.Operations.Clients;

public sealed class LogoutOperation(MizonDatabaseContext mizonDatabaseContext) 
    : IApiOperation<LogoutRequest, LogoutResponse>
{
    public async Task<BaseApiResponse<LogoutResponse>> HandleAsync(
        BaseApiRequest<LogoutRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var session = await mizonDatabaseContext.ClientSessions
            .Where(c => c.PublicId == requestMetadata!.SessionPublicId)
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

        return new BaseApiResponse<LogoutResponse>
        {
            Content = new()
        };
    }
}
