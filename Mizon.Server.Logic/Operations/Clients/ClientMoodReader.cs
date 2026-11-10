namespace Mizon.Server.Logic.Operations.Clients;

internal static class ClientMoodReader
{
    public static IQueryable<ClientMoodItemResponse> Select(IQueryable<ClientMoodType> query) =>
        query.Select(x => new ClientMoodItemResponse
        {
            PublicId = x.PublicId,
            Emoji = x.Emoji,
            Title = x.Title,
            PersianTitle = x.PersianTitle,
            AnimationMediaPublicId = x.AnimationMedia!.PublicId,
            PreviewMediaPublicId = x.PreviewMedia == null ? null : x.PreviewMedia.PublicId
        });

    public static async Task<ClientMoodItemResponse?> ReadAsync(MizonDatabaseContext database,
        int? typeId, CancellationToken cancellationToken)
    {
        if (typeId is null) return null;
        var mood = await Select(database.ClientMoodTypes.AsNoTracking()
            .Where(x => x.Id == typeId.Value)
            .Where(x => x.IsActive)).SingleOrDefaultAsync(cancellationToken);
        if (mood is null) return null;
        mood.EffectMediaPublicIds = await database.ClientMoodEffects.AsNoTracking()
            .Where(x => x.ClientMoodTypeId == typeId.Value)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => x.Media!.PublicId)
            .ToListAsync(cancellationToken);
        return mood;
    }
}

