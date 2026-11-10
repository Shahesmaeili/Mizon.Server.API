namespace Mizon.Server.Logic.Operations.Clients;

public sealed class ClientMoodTypesOperation(MizonDatabaseContext database)
    : IApiOperation<ClientMoodTypesRequest, ClientMoodTypesResponse>
{
    public async Task<BaseApiResponse<ClientMoodTypesResponse>> HandleAsync(
        BaseApiRequest<ClientMoodTypesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var items = await ClientMoodReader.Select(database.ClientMoodTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)).ToListAsync(cancellationToken);
        if (items.Count == 0)
            return new() { Content = new() { Items = items } };

        var effects = await database.ClientMoodEffects.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => x.ClientMoodType!.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { MoodPublicId = x.ClientMoodType!.PublicId, MediaPublicId = x.Media!.PublicId })
            .ToListAsync(cancellationToken);
        var effectLookup = effects.ToLookup(x => x.MoodPublicId, x => x.MediaPublicId);
        foreach (var item in items) item.EffectMediaPublicIds.AddRange(effectLookup[item.PublicId]);
        return new() { Content = new() { Items = items } };
    }
}

