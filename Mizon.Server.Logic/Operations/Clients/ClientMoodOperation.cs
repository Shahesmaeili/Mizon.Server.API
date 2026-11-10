namespace Mizon.Server.Logic.Operations.Clients;

// Persist desired state, including null to clear; a retry never toggles the selection.
public sealed class ClientMoodOperation(MizonDatabaseContext database)
    : IApiOperation<ClientMoodRequest, ClientMoodResponse>
{
    public async Task<BaseApiResponse<ClientMoodResponse>> HandleAsync(
        BaseApiRequest<ClientMoodRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId || requestMetadata.SessionPublicId is not Guid sessionPublicId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        if (!await database.ClientSessions.AsNoTracking()
            .Where(x => x.PublicId == sessionPublicId)
            .Where(x => x.ClientId == clientId)
            .Where(x => x.LogoutDateTime == null)
            .AnyAsync(cancellationToken))
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        if (request.Content is not { } content)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.InvalidClientMood } };

        int? moodId = null;
        if (content.ClientMoodTypePublicId is Guid publicId)
        {
            moodId = await database.ClientMoodTypes.AsNoTracking()
                .Where(x => x.PublicId == publicId)
                .Where(x => x.IsActive)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (moodId is null)
                return new() { Error = new() { Code = OperationErrorCodes.Clients.InvalidClientMood } };
        }

        var now = DateTimeOffset.UtcNow;
        await database.Clients
            .Where(x => x.Id == clientId)
            .Where(x => x.ClientMoodTypeId != moodId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ClientMoodTypeId, moodId)
                .SetProperty(x => x.UpdateDateTime, now), cancellationToken);
        var client = await database.Clients.AsNoTracking()
            .Where(x => x.Id == clientId)
            .Select(x => new { x.ClientMoodTypeId })
            .SingleOrDefaultAsync(cancellationToken);
        if (client is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        return new() { Content = new()
        {
            CurrentMood = await ClientMoodReader.ReadAsync(database, client.ClientMoodTypeId, cancellationToken)
        } };
    }
}

