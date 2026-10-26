namespace Mizon.Server.Logic.Operations.Clients;

public sealed class MeOperation(MizonDatabaseContext mizonDatabaseContext) 
    : IApiOperation<MeRequest, MeResponse>
{
    public async Task<BaseApiResponse<MeResponse>> HandleAsync(BaseApiRequest<MeRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var client = await mizonDatabaseContext.Clients
            .AsNoTracking()
            .Where(c => c.PublicId == requestMetadata!.ClientPublicId)
            .Select(c => new
            {
                c.Id,
                c.FirstName,
                c.LastName,
                c.NickName,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (client is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = 100,
                    Title = "",
                    Details = "",
                }
            };
        }


        var clientEmail = await mizonDatabaseContext.ClientEmails
             .AsNoTracking()
             .Where(c => c.ClientId == client.Id)
             .Where(c => c.IsActive)
             .Select(c => new
             {
                 c.Email,
                 c.IsVerified,
             })
             .FirstOrDefaultAsync(cancellationToken);

        if (clientEmail is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = 101,
                    Title = "",
                    Details = "",
                }
            };
        }

        var clientUsername = await mizonDatabaseContext.ClientUsernames
             .AsNoTracking()
             .Where(c => c.ClientId == client.Id)
             .Where(c => c.IsActive)
             .Select(c => new
             {
                 c.Username,
             })
             .FirstOrDefaultAsync(cancellationToken);


        var clientProfile = await mizonDatabaseContext.ClientProfiles
            .AsNoTracking()
            .Where(c => c.ClientId == client.Id)
            .Where(c => c.IsDefault)
            .Select(c => new
            {
                MediaPublicId = c.Media!.PublicId,
                c.Media.FileSize,
                c.Blurhash,
                c.Width,
                c.Height,
                c.PrimaryAccentColor,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                EmailAddress = clientEmail.Email,
                EmailAddressIsVerified = clientEmail.IsVerified,
                FirstName = client.FirstName,
                LastName = client.LastName,
                NickName = client.NickName,
                Username = clientUsername?.Username,
                MeProfile = clientProfile is null ? null : new()
                {
                    MediaPublicId = clientProfile.MediaPublicId,
                    FileSize = clientProfile.FileSize,
                    Blurhash = clientProfile.Blurhash,
                    Width = clientProfile.Width,
                    Height = clientProfile.Height,
                    PrimaryAccentColor = clientProfile.PrimaryAccentColor
                }
            }
        };
    }
}
