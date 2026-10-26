namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicRequest, MusicResponse>
{
    public async Task<BaseApiResponse<MusicResponse>> HandleAsync(BaseApiRequest<MusicRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var music = await mizonDatabaseContext.Musics
              .AsNoTracking()
              .Where(m => m.PublicId == request.Content!.MusicPublicId)
              .Select(m => new
              {
                  m.Id,
                  m.Title,
                  m.PersianTitle,
                  m.IsActive,
                  m.ReleaseDateTime,
                  m.MusicAlbumId,
                  m.MusicLabelId,
              })
              .FirstOrDefaultAsync(cancellationToken);

        if (music is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicNotFound,
                }
            };
        }

        if (!music.IsActive)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicNotActive,
                }
            };
        }


        var likesCount = await mizonDatabaseContext.MusicLikes
            .AsNoTracking()
            .Where(m => m.MusicId == music.Id)
            .CountAsync(cancellationToken);


        var likedByMe = await mizonDatabaseContext.MusicLikes
            .AsNoTracking()
            .Where(m => m.MusicId == music.Id)
            .Where(m => m.ClientId == requestMetadata!.ClientId!.Value)
            .AnyAsync(cancellationToken);

        var viewsCount = await mizonDatabaseContext.MusicViews
            .AsNoTracking()
            .Where(m => m.MusicId == music.Id)
            .CountAsync(cancellationToken);




        var musicGenres = await mizonDatabaseContext.MusicGenres
            .AsNoTracking()
            .Where(m => m.MusicId == music.Id)
            .Select(m => new MusicGenreResponse
            {
                Title = m.MusicGenreType!.Title,
                PersianTitle = m.MusicGenreType.PersianTitle,
                PublicId = m.MusicGenreType.PublicId,
            })
            .ToListAsync(cancellationToken);


        var musicLanguages = await mizonDatabaseContext.MusicLanguages
    .AsNoTracking()
    .Where(m => m.MusicId == music.Id)
    .Select(m => new MusicLanguageResponse
    {
        Title = m.Language!.Title,
        PersianTitle = m.Language.PersianTitle,
        PublicId = m.Language.PublicId,
    })
    .ToListAsync(cancellationToken);


        MusicAlbumResponse? musicAlbumResponse = null;

        if (music.MusicAlbumId > 0)
        {
            musicAlbumResponse = await mizonDatabaseContext.MusicAlbums
                .AsNoTracking()
                .Where(m => m.Id == music.MusicAlbumId)
                .Select(m => new MusicAlbumResponse
                {
                    Title = m.Title,
                    PersianTitle = m.PersianTitle,
                    PublicId = m.PublicId,
                })
                .FirstOrDefaultAsync(cancellationToken);
        }


        MusicLabelResponse? musicLabelResponse = null;

        if (music.MusicLabelId > 0)
        {
            musicLabelResponse = await mizonDatabaseContext.MusicLabels
                .AsNoTracking()
                .Where(m => m.Id == music.MusicLabelId)
                .Select(m => new MusicLabelResponse
                {
                    Title = m.Title,
                    PersianTitle = m.PersianTitle,
                    PublicId = m.PublicId,
                })
                .FirstOrDefaultAsync(cancellationToken);
        }


        var musicCoverResponse = await mizonDatabaseContext.MusicCovers
                  .AsNoTracking()
                  .Where(m => m.MusicId == music.Id)
                  .Select(m => new MusicCoverResponse
                  {
                      Blurhash = m.Blurhash,
                      FileSize = m.Media!.FileSize,
                      MediaPublicId = m.Media.PublicId,
                      Height = m.Height,
                      Width = m.Width,
                      PrimaryAccentColor = m.PrimaryAccentColor,
                  })
                  .FirstOrDefaultAsync(cancellationToken);


        return new()
        {
            Content = new()
            {
                Title = music.Title,
                PersianTitle = music.PersianTitle,
                ReleaseDateTime = music.ReleaseDateTime,
                LikedByMe = likedByMe,
                LikesCount = likesCount,
                ViewsCount = viewsCount,
                MusicGenres = musicGenres,
                MusicAlbum = musicAlbumResponse,
                MusicLabel = musicLabelResponse,
                MusicLanguages = musicLanguages,
                MusicCover = musicCoverResponse
            }
        };
    }
}