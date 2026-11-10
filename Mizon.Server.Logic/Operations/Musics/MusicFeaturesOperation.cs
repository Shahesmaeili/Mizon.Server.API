namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicFeaturesOperation(MizonDatabaseContext database)
    : IApiOperation<MusicFeaturesRequest, MusicFeaturesResponse>
{
    public async Task<BaseApiResponse<MusicFeaturesResponse>> HandleAsync(
        BaseApiRequest<MusicFeaturesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };

        var now = DateTimeOffset.UtcNow;
        var limit = Math.Clamp(request.Content?.Limit ?? 12, 1, 12);
        var features = await database.MusicFeatures.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => x.Music!.IsActive)
            .Where(x => x.StartDateTime == null || x.StartDateTime <= now)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > now)
            .Where(x => x.MusicFeatureContentType!.Code == "image" || x.MusicFeatureContentType.Code == "video")
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.PublicId, x.MusicId,
                MediaPublicId = x.ContentMedia!.PublicId,
                PreviewMediaPublicId = x.PreviewMedia == null ? (Guid?)null : x.PreviewMedia.PublicId,
                ContentTypeCode = x.MusicFeatureContentType!.Code, x.DisplaySeconds, x.ExpireDateTime
            })
            .Take(limit)
            .ToListAsync(cancellationToken);
        // A cached carousel must refresh when a scheduled feature starts, even
        // when the current page is empty. Expired-before-start rows cannot appear.
        var nextStartDateTime = await database.MusicFeatures.AsNoTracking()
            .Where(x => x.IsActive)
            .Where(x => x.Music!.IsActive)
            .Where(x => x.MusicFeatureContentType!.Code == "image" || x.MusicFeatureContentType.Code == "video")
            .Where(x => x.StartDateTime > now)
            .Where(x => x.ExpireDateTime == null || x.ExpireDateTime > x.StartDateTime)
            .OrderBy(x => x.StartDateTime)
            .Select(x => x.StartDateTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (features.Count == 0) return new() { CacheExpiresAt = nextStartDateTime, Content = new() };
        var cacheExpiresAt = features.Min(feature => feature.ExpireDateTime);
        if (nextStartDateTime is DateTimeOffset startsAt
            && (cacheExpiresAt is null || startsAt < cacheExpiresAt.Value))
            cacheExpiresAt = startsAt;

        var musicIds = features.Select(x => x.MusicId).Distinct().ToArray();
        var musics = await database.Musics.AsNoTracking()
            .Where(x => musicIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.Title, x.PersianTitle })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var covers = await database.MusicCovers.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .Select(x => new { x.MusicId, MediaPublicId = x.Media!.PublicId, x.PrimaryAccentColor })
            .ToDictionaryAsync(x => x.MusicId, cancellationToken);
        var durations = await database.MusicFiles.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .GroupBy(x => x.MusicId)
            .Select(group => new
            {
                MusicId = group.Key,
                Duration = group.OrderBy(file => file.Id).Select(file => file.Duration).First()
            })
            .ToDictionaryAsync(x => x.MusicId, x => x.Duration.TotalSeconds, cancellationToken);
        var likes = await database.MusicLikes.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .GroupBy(x => x.MusicId)
            .Select(x => new { MusicId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.MusicId, x => x.Count, cancellationToken);
        var views = await database.MusicViews.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .GroupBy(x => x.MusicId)
            .Select(x => new { MusicId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.MusicId, x => x.Count, cancellationToken);
        var likedIds = await database.MusicLikes.AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .Where(x => musicIds.Contains(x.MusicId))
            .Select(x => x.MusicId)
            .ToHashSetAsync(cancellationToken);
        var contributors = await database.MusicContributors.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .Where(x => x.MusicContributorType!.Title == "LeadVocal" || x.MusicContributorType.Title == "FeaturedVocal")
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.MusicId, PersonPublicId = x.Person!.PublicId, x.Person.FirstName, x.Person.LastName,
                ProfileMediaPublicId = x.Person.ProfileMedia == null ? (Guid?)null : x.Person.ProfileMedia.PublicId
            })
            .ToListAsync(cancellationToken);
        var genres = await database.MusicGenres.AsNoTracking()
            .Where(x => musicIds.Contains(x.MusicId))
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.MusicId, x.MusicGenreType!.PublicId, x.MusicGenreType.Title, x.MusicGenreType.PersianTitle
            })
            .ToListAsync(cancellationToken);

        var artistLookup = contributors.GroupBy(x => x.MusicId).ToDictionary(x => x.Key,
            x => x.DistinctBy(y => y.PersonPublicId).Select(y => new MusicFeatureContributorResponse
            {
                PersonPublicId = y.PersonPublicId, FirstName = y.FirstName, LastName = y.LastName,
                ProfileMediaPublicId = y.ProfileMediaPublicId
            }).ToList());
        var genreLookup = genres.GroupBy(x => x.MusicId).ToDictionary(x => x.Key,
            x => x.Select(y => new MusicFeatureGenreResponse
            {
                PublicId = y.PublicId, Title = y.Title, PersianTitle = y.PersianTitle
            }).ToList());

        return new()
        {
            CacheExpiresAt = cacheExpiresAt,
            Content = new()
            {
                Items = features.Select(feature =>
                {
                    var music = musics[feature.MusicId];
                    covers.TryGetValue(feature.MusicId, out var cover);
                    return new MusicFeatureItemResponse
                    {
                        PublicId = feature.PublicId, MusicPublicId = music.PublicId,
                        MediaPublicId = feature.MediaPublicId, PreviewMediaPublicId = feature.PreviewMediaPublicId ?? cover?.MediaPublicId,
                        ContentTypeCode = feature.ContentTypeCode, DisplaySeconds = Math.Clamp(feature.DisplaySeconds, 3, 60),
                        Title = music.Title, PersianTitle = music.PersianTitle,
                        DurationSeconds = durations.TryGetValue(feature.MusicId, out var duration) ? duration : null,
                        CoverMediaPublicId = cover?.MediaPublicId, PrimaryAccentColor = cover?.PrimaryAccentColor,
                        LikesCount = likes.GetValueOrDefault(feature.MusicId), ViewsCount = views.GetValueOrDefault(feature.MusicId),
                        LikedByMe = likedIds.Contains(feature.MusicId),
                        Contributors = artistLookup.GetValueOrDefault(feature.MusicId) ?? [],
                        Genres = genreLookup.GetValueOrDefault(feature.MusicId) ?? []
                    };
                }).ToList()
            }
        };
    }
}
