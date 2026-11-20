using Microsoft.Data.SqlClient;

namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentVoteOperation(MizonDatabaseContext database)
    : IApiOperation<MusicCommentVoteRequest, MusicCommentVoteResponse>
{
    public async Task<BaseApiResponse<MusicCommentVoteResponse>> HandleAsync(BaseApiRequest<MusicCommentVoteRequest> request,
        RequestMetadata? metadata, CancellationToken token)
    {
        if (request.Content is not { Vote: >= -1 and <= 1 } input || metadata?.ClientId is not int clientId)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };

        var commentId = await database.MusicComments.AsNoTracking()
            .Where(comment => comment.PublicId == input.MusicCommentPublicId)
            .Where(comment => comment.Music!.PublicId == input.MusicPublicId)
            .Where(comment => comment.Music!.IsActive).Select(comment => (int?)comment.Id).SingleOrDefaultAsync(token);
        if (commentId is not int id)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicCommentNotFound } };

        var own = database.MusicCommentVotes.Where(vote => vote.MusicCommentId == id)
            .Where(vote => vote.ClientId == clientId);
        if (input.Vote == 0)
            await own.ExecuteDeleteAsync(token);
        else
        {
            var isLike = input.Vote == 1;
            for (var attempt = 0; ; attempt++)
            {
                var updatedAt = DateTimeOffset.UtcNow;
                if (await own.ExecuteUpdateAsync(set => set.SetProperty(vote => vote.IsLike, isLike)
                    .SetProperty(vote => vote.UpdateDateTime, updatedAt), token) != 0)
                    break;

                var row = new MusicCommentVote { MusicCommentId = id, ClientId = clientId, IsLike = isLike };
                database.MusicCommentVotes.Add(row);
                try
                {
                    await database.SaveChangesAsync(token);
                    break;
                }
                catch (DbUpdateException error) when (attempt < 2 &&
                    error.InnerException is SqlException { Number: 2601 or 2627 })
                {
                    database.Entry(row).State = EntityState.Detached;
                }
            }
        }

        var result = await database.MusicComments.AsNoTracking().Where(comment => comment.Id == id)
            .Select(comment => new MusicCommentVoteResponse
            {
                Likes = comment.Votes.Count(vote => vote.IsLike),
                Dislikes = comment.Votes.Count(vote => !vote.IsLike),
                MyVote = comment.Votes.Where(vote => vote.ClientId == clientId)
                    .Select(vote => vote.IsLike ? 1 : -1).FirstOrDefault()
            }).SingleAsync(token);
        return new() { Content = result };
    }
}
