namespace Mizon.Server.Logic.Operations.Clients;

public sealed class ClientProfileOperation(MizonDatabaseContext database)
    : IApiOperation<ClientProfileRequest, ClientProfileResponse>
{
    public async Task<BaseApiResponse<ClientProfileResponse>> HandleAsync(
        BaseApiRequest<ClientProfileRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientPublicId is null)
            return new() { Error = new() { Code = 401, Title = "Unauthorized" } };

        var clientPublicId = request.Content?.ClientPublicId ?? Guid.Empty;
        if (clientPublicId == Guid.Empty)
            return new() { Error = new() { Code = 400, Title = "ClientPublicId is required" } };

        var client = await database.Clients
            .AsNoTracking()
            .Where(client => client.PublicId == clientPublicId)
            .Select(client => new { client.Id, client.PublicId, client.FirstName, client.LastName, client.NickName })
            .FirstOrDefaultAsync(cancellationToken);
        if (client is null)
            return new() { Error = new() { Code = 404, Title = "Client not found" } };

        var username = await database.ClientUsernames
            .AsNoTracking()
            .Where(username => username.ClientId == client.Id)
            .Where(username => username.IsActive)
            .OrderByDescending(username => username.Id)
            .Select(username => username.Username)
            .FirstOrDefaultAsync(cancellationToken);

        var profile = await database.ClientProfiles
            .AsNoTracking()
            .Where(profile => profile.ClientId == client.Id)
            .Where(profile => profile.IsDefault)
            .OrderByDescending(profile => profile.Id)
            .Select(profile => new { MediaPublicId = profile.Media!.PublicId, profile.PrimaryAccentColor })
            .FirstOrDefaultAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                ClientPublicId = client.PublicId,
                FirstName = client.FirstName,
                LastName = client.LastName,
                NickName = client.NickName,
                Username = username,
                ProfileMediaPublicId = profile?.MediaPublicId,
                PrimaryAccentColor = profile?.PrimaryAccentColor
            }
        };
    }
}
