namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicCommentsOperation(MizonDatabaseContext database)
    : IApiOperation<MusicCommentsRequest, MusicCommentsResponse>
{
    public async Task<BaseApiResponse<MusicCommentsResponse>> HandleAsync(BaseApiRequest<MusicCommentsRequest> request,
        RequestMetadata? metadata, CancellationToken token)
    {
        if (request.Content is not { } input || input.MusicPublicId == Guid.Empty ||
            input.PageSize is < 1 or > 100 || input.Sort is not (null or "newest" or "oldest" or "likes" or "dislikes") ||
            (input.After is not null && input.PageSize is null) ||
            (input.MusicCommentPublicId is not null && (input.After is not null || input.ParentMusicCommentPublicId is not null)))
            return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };

        var musicId = await database.Musics.AsNoTracking()
            .Where(music => music.PublicId == input.MusicPublicId)
            .Where(music => music.IsActive).Select(music => (int?)music.Id).SingleOrDefaultAsync(token);
        if (musicId is null)
            return new() { Error = new() { Code = OperationErrorCodes.Musics.MusicNotFound } };

        var comments = database.MusicComments.AsNoTracking().Where(comment => comment.MusicId == musicId);
        var paged = input.PageSize.HasValue && input.MusicCommentPublicId is null;
        if (input.MusicCommentPublicId is Guid exact)
            comments = comments.Where(comment => comment.PublicId == exact);
        else if (input.ParentMusicCommentPublicId is Guid parent)
            comments = comments.Where(comment => comment.ParentMusicComment!.PublicId == parent);
        else if (input.TimedOnly != true && paged)
            comments = comments.Where(comment => comment.ParentMusicCommentId == null);
        if (input.TimedOnly == true)
            comments = comments.Where(comment => comment.Timestamp != null);

        if (input.After is Guid after)
        {
            var key = await comments.Where(comment => comment.PublicId == after)
                .Select(comment => new
                {
                    comment.Id,
                    Likes = input.Sort == "likes" ? comment.Votes.Count(vote => vote.IsLike) : 0,
                    Dislikes = input.Sort == "dislikes" ? comment.Votes.Count(vote => !vote.IsLike) : 0
                }).SingleOrDefaultAsync(token);
            if (key is null)
                return new() { Error = new() { Code = OperationErrorCodes.Musics.InvalidMusicCommentRequest } };
            comments = input.Sort switch
            {
                "oldest" => comments.Where(comment => comment.Id > key.Id),
                "likes" => comments.Where(comment => comment.Votes.Count(vote => vote.IsLike) < key.Likes ||
                    (comment.Votes.Count(vote => vote.IsLike) == key.Likes && comment.Id < key.Id)),
                "dislikes" => comments.Where(comment => comment.Votes.Count(vote => !vote.IsLike) < key.Dislikes ||
                    (comment.Votes.Count(vote => !vote.IsLike) == key.Dislikes && comment.Id < key.Id)),
                _ => comments.Where(comment => comment.Id < key.Id)
            };
        }

        IQueryable<MusicComment> ordered = input.Sort switch
        {
            "likes" => comments.OrderByDescending(comment => comment.Votes.Count(vote => vote.IsLike)).ThenByDescending(comment => comment.Id),
            "dislikes" => comments.OrderByDescending(comment => comment.Votes.Count(vote => !vote.IsLike)).ThenByDescending(comment => comment.Id),
            "oldest" => comments.OrderBy(comment => comment.Id),
            _ when paged => comments.OrderByDescending(comment => comment.Id),
            _ => comments.OrderBy(comment => comment.Timestamp).ThenBy(comment => comment.Id)
        };
        if (input.PageSize is int size) ordered = ordered.Take(size + 1);
        var clientId = metadata?.ClientId;
        var details = input.TimedOnly != true && (paged || input.MusicCommentPublicId is not null);
        var rows = await ordered.Select(comment => new
        {
            comment.Id,
            Item = new MusicCommentItemResponse
            {
                PublicId = comment.PublicId,
                Text = comment.Text,
                Timestamp = comment.Timestamp,
                CreateDateTime = comment.CreateDateTime,
                ClientPublicId = comment.Client!.PublicId,
                FirstName = comment.Client.FirstName,
                LastName = comment.Client.LastName,
                Username = comment.Client.ClientUsernames.Where(username => username.IsActive)
                    .OrderByDescending(username => username.Id).Select(username => username.Username).FirstOrDefault(),
                ProfileMediaPublicId = comment.Client.ClientProfiles.Where(profile => profile.IsDefault)
                    .Select(profile => (Guid?)profile.Media!.PublicId).FirstOrDefault(),
                ParentMusicCommentPublicId = details ? (Guid?)comment.ParentMusicComment!.PublicId : null,
                Platform = details ? comment.PlatformVersion!.Platform!.Title : null,
                Replies = details ? comment.Replies.Count : 0,
                Likes = details ? comment.Votes.Count(vote => vote.IsLike) : 0,
                Dislikes = details ? comment.Votes.Count(vote => !vote.IsLike) : 0,
                MyVote = details ? comment.Votes.Where(vote => vote.ClientId == clientId)
                    .Select(vote => vote.IsLike ? 1 : -1).FirstOrDefault() : 0
            }
        }).ToListAsync(token);

        Guid? next = null;
        if (input.PageSize is int limit && rows.Count > limit)
        {
            rows.RemoveAt(limit);
            next = rows[^1].Item.PublicId;
        }
        if (details && rows.Count != 0)
        {
            var items = rows.ToDictionary(row => row.Id, row => row.Item);
            await MusicCommentTags.LoadAsync(database, items, token);
        }
        return new() { Content = new() { MusicComments = rows.Select(row => row.Item).ToList(), Next = next } };
    }
}
