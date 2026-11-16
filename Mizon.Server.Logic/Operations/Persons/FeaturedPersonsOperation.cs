using Mizon.Server.Logic.Requests.Persons;
using Mizon.Server.Logic.Responses.Persons;

namespace Mizon.Server.Logic.Operations.Persons;

public sealed class FeaturedPersonsOperation(MizonDatabaseContext database)
    : IApiOperation<FeaturedPersonsRequest, FeaturedPersonsResponse>
{
    private const int MaxPersons = 12;
    private const int MaxTracks = 3;
    private const int TrendDays = 7;

    public async Task<BaseApiResponse<FeaturedPersonsResponse>> HandleAsync(BaseApiRequest<FeaturedPersonsRequest> request,
        RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        var code = request.Content?.TypeCode?.Trim().ToLowerInvariant();
        if (code is not ("music" or "movies" or "series"))
            return new() { Error = new() { Code = OperationErrorCodes.Persons.InvalidRequest } };

        int? viewer = null;
        if (metadata is { ClientId: int clientId, SessionPublicId: Guid session }
            && await PersonProfileQueries.HasActiveSessionAsync(database, clientId, session, cancellationToken))
            viewer = clientId;

        var music = code == "music";
        var follows = PersonProfileQueries.ActiveFollows(database);
        var people = await database.FeaturedPersons.AsNoTracking()
            .Where(x => x.FeaturedPersonType!.Code == code)
            .OrderBy(x => x.SortOrder).Take(MaxPersons)
            .Select(x => new
            {
                x.PersonId,
                Item = new FeaturedPersonItem
                {
                    PersonPublicId = x.Person!.PublicId, SortOrder = x.SortOrder,
                    FirstName = x.Person.FirstName, LastName = x.Person.LastName,
                    ProfileMediaPublicId = x.Person.ProfileMedia == null ? null : x.Person.ProfileMedia.PublicId,
                    CoverMediaPublicId = x.Person.CoverMedia == null ? null : x.Person.CoverMedia.PublicId,
                    ModelMediaPublicId = x.Person.ModelMedia == null ? null : x.Person.ModelMedia.PublicId,
                    AccentColor = x.AccentColor, ModelAttribution = x.ModelAttribution,
                    ModelYawInDegrees = x.ModelYawInDegrees, ModelViewScale = x.ModelViewScale,
                    ModelVerticalFocus = x.ModelVerticalFocus,
                    FollowersCount = follows.Count(f => f.FollowedPersonId == x.PersonId),
                    IsFollowing = viewer != null && follows.Any(f => f.FollowedPersonId == x.PersonId && f.FollowerClientId == viewer),
                    CanFollow = viewer != null && (x.Person.LinkedClientId != viewer
                        || follows.Any(f => f.FollowedPersonId == x.PersonId && f.FollowerClientId == viewer)),
                    MusicsCount = music ? x.Person.MusicContributors.Where(c => c.Music!.IsActive)
                        .Select(c => c.MusicId).Distinct().Count() : 0
                }
            }).ToListAsync(cancellationToken);

        if (music && people.Count > 0)
            await LoadMusicAsync(people.Where(x => x.Item.MusicsCount > 0).ToDictionary(x => x.PersonId, x => x.Item), cancellationToken);

        return new() { Content = new() { Persons = people.Select(x => x.Item).ToList() } };
    }

    private async Task LoadMusicAsync(Dictionary<int, FeaturedPersonItem> people, CancellationToken token)
    {
        if (people.Count == 0) return;
        var ids = people.Keys.ToArray();
        var credits = database.MusicContributors.AsNoTracking().Where(c => ids.Contains(c.PersonId) && c.Music!.IsActive)
            .Select(c => new { c.PersonId, c.MusicId }).Distinct();
        var tracks = await database.Persons.AsNoTracking().Where(p => ids.Contains(p.Id))
            .SelectMany(p => credits.Where(c => c.PersonId == p.Id)
                .Join(database.Musics.AsNoTracking(), c => c.MusicId, m => m.Id, (c, m) => m)
                .OrderByDescending(m => m.ReleaseDateTime ?? m.CreateDateTime).ThenByDescending(m => m.Id).Take(MaxTracks)
                .Select(m => new
                {
                    PersonId = p.Id, m.Id, Published = m.ReleaseDateTime ?? m.CreateDateTime,
                    Genre = m.MusicGenres.OrderBy(g => g.Id).Select(g => new
                    {
                        g.MusicGenreType!.Title, g.MusicGenreType.PersianTitle
                    }).FirstOrDefault(),
                    Item = new FeaturedPersonMusicItem
                    {
                        PublicId = m.PublicId, Title = m.Title, PersianTitle = m.PersianTitle,
                        CoverMediaPublicId = m.MusicCover == null ? null : m.MusicCover.Media!.PublicId,
                        Duration = m.MusicFiles.OrderBy(f => f.Id).Select(f => (TimeSpan?)f.Duration).FirstOrDefault(),
                        LikesCount = m.MusicLikes.Count
                    }
                }))
            .OrderByDescending(x => x.Published).ThenByDescending(x => x.Id).ToListAsync(token);
        foreach (var track in tracks)
        {
            var person = people[track.PersonId];
            person.LatestMusics.Add(track.Item);
            if (person.GenreTitle is null && track.Genre is not null)
            {
                person.GenreTitle = track.Genre.Title;
                person.PersianGenreTitle = track.Genre.PersianTitle;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var firstDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1 - TrendDays), TimeSpan.Zero);
        var monthStart = now.AddDays(-30);
        var plays = from credit in credits
                    join play in database.MusicPlays.AsNoTracking() on credit.MusicId equals play.MusicId
                    where play.CreateDateTime >= monthStart && play.CreateDateTime <= now
                    select new { credit.PersonId, play.ClientId, play.CreateDateTime };
        var audience = await plays.Select(x => new { x.PersonId, x.ClientId }).Distinct()
            .GroupBy(x => x.PersonId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(token);
        foreach (var row in audience) people[row.Id].MonthlyListenersCount = row.Count;

        var dailyPlays = await plays.Where(x => x.CreateDateTime >= firstDay)
            .GroupBy(x => new { x.PersonId, Day = EF.Functions.DateDiffDay(firstDay, x.CreateDateTime) })
            .Select(g => new { g.Key.PersonId, g.Key.Day, Count = g.Count() }).ToListAsync(token);
        foreach (var person in people.Values) person.DailyPlays = new int[TrendDays];
        foreach (var row in dailyPlays)
            if ((uint)row.Day < TrendDays) people[row.PersonId].DailyPlays[row.Day] = row.Count;
    }
}
