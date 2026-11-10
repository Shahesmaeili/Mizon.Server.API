namespace Mizon.Server.Logic.Operations.Clients;

public sealed class FindClientIdOperation(MizonDatabaseContext mizonDatabaseContext) 
    : IApiOperation<FindClientIdRequest, FindClientIdResponse>
{
    public async Task<BaseApiResponse<FindClientIdResponse>> HandleAsync(BaseApiRequest<FindClientIdRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var clientId = await mizonDatabaseContext.Clients
            .AsNoTracking()
            .Where(c => c.PublicId == request.Content!.ClientPublicId)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (clientId is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = 100,
                    Title = "",
                    Details = "",
                }
            };
        }

        return new()
        {
            Content = new()
            {
                ClientId = clientId.Value,
            }
        };
    }
}
