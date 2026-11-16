using Mizon.Server.Logic.Requests.Persons;
using Mizon.Server.Logic.Responses.Persons;

namespace Mizon.Server.Logic.Operations.Persons;

public sealed class PersonProfileOperation(MizonDatabaseContext database)
    : IApiOperation<PersonProfileRequest, PersonProfileResponse>
{
    public async Task<BaseApiResponse<PersonProfileResponse>> HandleAsync(BaseApiRequest<PersonProfileRequest> request,
        RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        if (request.Content is not { PersonPublicId: var publicId } || publicId == Guid.Empty)
            return new() { Error = new() { Code = OperationErrorCodes.Persons.InvalidRequest } };

        var viewerClientId = metadata is { ClientId: int viewer, SessionPublicId: Guid session }
            && await PersonProfileQueries.HasActiveSessionAsync(database, viewer, session, cancellationToken) ? (int?)viewer : null;
        var person = await database.Persons.AsNoTracking()
            .Where(x => x.PublicId == publicId)
            .Select(x => new
            {
                x.Id, x.LinkedClientId,
                CanReadFollowing = x.LinkedClientId != null
                    && (x.LinkedClient!.ClientPrivacyTypeId == null || x.LinkedClient.ClientPrivacyType!.Title == "Public"
                        || x.LinkedClientId == viewerClientId),
                Profile = new PersonProfileResponse
                {
                    PersonPublicId = x.PublicId, FirstName = x.FirstName, LastName = x.LastName,
                    ProfileMediaPublicId = x.ProfileMedia == null ? null : x.ProfileMedia.PublicId,
                    CoverMediaPublicId = x.CoverMedia == null ? null : x.CoverMedia.PublicId,
                    ModelMediaPublicId = x.ModelMedia == null ? null : x.ModelMedia.PublicId
                }
            }).SingleOrDefaultAsync(cancellationToken);

        if (person is null) return new() { Error = new() { Code = OperationErrorCodes.Persons.NotFound } };

        var profile = person.Profile;
        var followers = PersonProfileQueries.ActiveFollows(database).Where(x => x.FollowedPersonId == person.Id);
        profile.FollowersCount = await followers.CountAsync(cancellationToken);
        if (viewerClientId is int clientId)
        {
            profile.IsFollowing = await followers.Where(x => x.FollowerClientId == clientId).AnyAsync(cancellationToken);
            profile.CanFollow = person.LinkedClientId != clientId || profile.IsFollowing;
        }
        if (person.CanReadFollowing)
        {
            profile.FollowingCount = await PersonProfileQueries.ActiveFollows(database)
                .Where(x => x.FollowerClientId == person.LinkedClientId).CountAsync(cancellationToken);
        }
        profile.MusicsCount = await PersonProfileQueries.MusicIds(database, person.Id).CountAsync(cancellationToken);
        return new() { Content = profile };
    }
}

internal static class PersonProfileQueries
{
    public static Task<bool> HasActiveSessionAsync(MizonDatabaseContext database, int clientId, Guid sessionPublicId,
        CancellationToken cancellationToken) => database.ClientSessions.AsNoTracking().Where(x => x.PublicId == sessionPublicId)
        .Where(x => x.ClientId == clientId).Where(x => x.LogoutDateTime == null).AnyAsync(cancellationToken);

    public static IQueryable<ClientFollow> ActiveFollows(MizonDatabaseContext database) =>
        database.ClientFollows.AsNoTracking().Where(x => x.FollowDateTime != null).Where(x => x.EndDateTime == null);

    public static IQueryable<int> MusicIds(MizonDatabaseContext database, int personId) =>
        database.MusicContributors.AsNoTracking().Where(credit => credit.PersonId == personId)
            .Where(credit => credit.Music!.IsActive).Select(credit => credit.MusicId).Distinct();
}
