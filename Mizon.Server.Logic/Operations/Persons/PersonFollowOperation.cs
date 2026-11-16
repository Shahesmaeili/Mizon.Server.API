using System.Data;
using Microsoft.Data.SqlClient;
using Mizon.Server.Logic.Requests.Persons;
using Mizon.Server.Logic.Responses.Persons;

namespace Mizon.Server.Logic.Operations.Persons;

public sealed class PersonFollowOperation(MizonDatabaseContext database)
    : IApiOperation<PersonFollowRequest, PersonFollowResponse>
{
    public async Task<BaseApiResponse<PersonFollowResponse>> HandleAsync(BaseApiRequest<PersonFollowRequest> request,
        RequestMetadata? metadata, CancellationToken cancellationToken)
    {
        if (metadata is not { ClientId: int clientId, SessionPublicId: Guid sessionPublicId })
            return Error(OperationErrorCodes.Clients.ClientSessionNotFound);
        if (request.Content is not { PersonPublicId: var publicId, Following: bool following } || publicId == Guid.Empty)
            return Error(OperationErrorCodes.Persons.InvalidRequest);

        for (var attempt = 0; ; attempt++)
        {
            try { return await SaveAsync(publicId, clientId, sessionPublicId, following, cancellationToken); }
            catch (Exception error) when (IsWriteConflict(error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                database.ChangeTracker.Clear();
                if (attempt == 4) return Error(OperationErrorCodes.Persons.FollowConflict);
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(15, 36) * (attempt + 1)), cancellationToken);
            }
        }
    }

    private async Task<BaseApiResponse<PersonFollowResponse>> SaveAsync(Guid personPublicId, int clientId, Guid sessionPublicId, bool following,
        CancellationToken cancellationToken)
    {
        // The unique pair and rowversion protect the follow record. Serializable
        // also protects first use of reference-data rows and the resulting count.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await PersonProfileQueries.HasActiveSessionAsync(database, clientId, sessionPublicId, cancellationToken))
            return Error(OperationErrorCodes.Clients.ClientSessionNotFound);

        var person = await database.Persons.AsNoTracking().Where(x => x.PublicId == personPublicId)
            .Select(x => new { x.Id, x.LinkedClientId }).SingleOrDefaultAsync(cancellationToken);
        if (person is null) return Error(OperationErrorCodes.Persons.NotFound);
        if (following && person.LinkedClientId == clientId) return Error(OperationErrorCodes.Persons.SelfFollow);

        var row = await database.ClientFollows.Where(x => x.FollowerClientId == clientId)
            .Where(x => x.FollowedPersonId == person.Id).SingleOrDefaultAsync(cancellationToken);
        if (following != (row?.FollowDateTime is not null && row.EndDateTime is null))
        {
            var (statusId, actionId) = await EnsureCatalogAsync(following, cancellationToken);
            byte? previousStatusId = row?.ClientFollowStatusTypeId;
            if (row is null)
            {
                row = new ClientFollow { PublicId = Guid.CreateVersion7(), FollowerClientId = clientId, FollowedPersonId = person.Id };
                database.ClientFollows.Add(row);
            }

            var now = DateTimeOffset.UtcNow;
            row.ClientFollowStatusTypeId = statusId;
            if (following)
            {
                row.RequestDateTime = null;
                row.AcceptDateTime = null;
                row.FollowDateTime = now;
                row.EndDateTime = null;
            }
            else
                row.EndDateTime = now;

            database.ClientFollowHistories.Add(new()
            {
                ClientFollow = row, ClientFollowActionTypeId = actionId,
                PreviousClientFollowStatusTypeId = previousStatusId,
                ClientFollowStatusTypeId = statusId, ActorClientId = clientId
            });
            await database.SaveChangesAsync(cancellationToken);
        }

        var count = await PersonProfileQueries.ActiveFollows(database)
            .Where(x => x.FollowedPersonId == person.Id).CountAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new() { Content = new() { IsFollowing = following, FollowersCount = count } };
    }

    private async Task<(byte Status, byte Action)> EnsureCatalogAsync(bool following, CancellationToken cancellationToken)
    {
        var statusTitle = following ? "Following" : "Unfollowed";
        var actionTitle = following ? "Follow" : "Unfollow";
        var status = await database.ClientFollowStatusTypes.AsNoTracking().Where(x => x.Title == statusTitle)
            .OrderBy(x => x.Id).Select(x => (byte?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!status.HasValue)
        {
            var ids = await database.ClientFollowStatusTypes.Select(x => x.Id).ToListAsync(cancellationToken);
            status = FirstAvailableId(ids);
            database.ClientFollowStatusTypes.Add(new()
            {
                Id = status.Value, PublicId = Guid.CreateVersion7(), Title = statusTitle,
                PersianTitle = following ? "دنبال‌شده" : "لغو دنبال‌کردن"
            });
        }

        var action = await database.ClientFollowActionTypes.AsNoTracking().Where(x => x.Title == actionTitle)
            .OrderBy(x => x.Id).Select(x => (byte?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!action.HasValue)
        {
            var ids = await database.ClientFollowActionTypes.Select(x => x.Id).ToListAsync(cancellationToken);
            action = FirstAvailableId(ids);
            database.ClientFollowActionTypes.Add(new()
            {
                Id = action.Value, PublicId = Guid.CreateVersion7(), Title = actionTitle,
                PersianTitle = following ? "دنبال‌کردن" : "لغو دنبال‌کردن"
            });
        }
        // Both reference rows must exist before saving their dependent history.
        if (database.ChangeTracker.HasChanges()) await database.SaveChangesAsync(cancellationToken);
        return (status.Value, action.Value);
    }

    private static byte FirstAvailableId(List<byte> used)
    {
        var occupied = used.ToHashSet();
        for (var id = 1; id <= byte.MaxValue; id++)
            if (!occupied.Contains((byte)id)) return (byte)id;
        throw new InvalidOperationException("The follow lookup table has no available IDs.");
    }

    private static bool IsWriteConflict(Exception error)
    {
        // SQL Server's strategy can wrap a deadlock in both a transient-operation
        // exception and DbUpdateException. Retry the entire transaction.
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException
                || current is SqlException { Number: 1205 or 2601 or 2627 })
                return true;
        return false;
    }

    private static BaseApiResponse<PersonFollowResponse> Error(int code) => new() { Error = new() { Code = code } };
}
