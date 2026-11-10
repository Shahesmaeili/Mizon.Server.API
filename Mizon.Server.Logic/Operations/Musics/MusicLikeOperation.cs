using Microsoft.Data.SqlClient;

namespace Mizon.Server.Logic.Operations.Musics;

// Desired state rather than a toggle makes retries safe.
public sealed class MusicLikeOperation(MizonDatabaseContext database)
    : IApiOperation<MusicLikeRequest, MusicLikeResponse>
{
    public async Task<BaseApiResponse<MusicLikeResponse>> HandleAsync(
        BaseApiRequest<MusicLikeRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        if (requestMetadata?.ClientId is not int clientId || requestMetadata.SessionPublicId is not Guid sessionPublicId)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        if (request.Content is not { } content)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var platformVersionId = await database.ClientSessions.AsNoTracking()
            .Where(x => x.PublicId == sessionPublicId)
            .Where(x => x.ClientId == clientId)
            .Where(x => x.LogoutDateTime == null)
            .Select(x => (short?)x.PlatformVersionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (platformVersionId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Clients.ClientSessionNotFound } };
        var musicId = await database.Musics.AsNoTracking()
            .Where(x => x.PublicId == content.MusicPublicId)
            .Where(x => x.IsActive)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (musicId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var ownLike = database.MusicLikes.Where(x => x.MusicId == musicId.Value).Where(x => x.ClientId == clientId);
        if (content.Liked)
        {
            if (!await ownLike.AsNoTracking().AnyAsync(cancellationToken))
            {
                var like = new MusicLike { ClientId = clientId, MusicId = musicId.Value, PlatformVersionId = platformVersionId.Value };
                database.MusicLikes.Add(like);
                try { await database.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 } sql
                    && sql.Message.Contains("IX_MusicLikes_MusicId_ClientId", StringComparison.Ordinal))
                {
                    database.Entry(like).State = EntityState.Detached;
                    // A concurrent identical like already committed. Report the current
                    // persisted state below, even if a later unlike has since removed it.
                }
            }
        }
        else await ownLike.ExecuteDeleteAsync(cancellationToken);

        var likedByMe = await ownLike.AsNoTracking().AnyAsync(cancellationToken);
        var likesCount = await database.MusicLikes.AsNoTracking()
            .Where(x => x.MusicId == musicId.Value).CountAsync(cancellationToken);
        return new() { Content = new() { MusicPublicId = content.MusicPublicId, LikedByMe = likedByMe, LikesCount = likesCount } };
    }
}
