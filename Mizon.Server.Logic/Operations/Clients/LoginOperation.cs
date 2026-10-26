using System.Security.Cryptography;

namespace Mizon.Server.Logic.Operations.Clients;

/// <summary>
/// Represents login operation.
/// </summary>
public sealed class LoginOperation(MizonDatabaseContext mizonDatabaseContext) 
    : IApiOperation<LoginRequest, LoginResponse>
{
    public async Task<BaseApiResponse<LoginResponse>> HandleAsync(BaseApiRequest<LoginRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        int? clientId = null;
        Guid? clientPublicId = null;


        var platformVersion = await mizonDatabaseContext.PlatformVersions
               .AsNoTracking()
               .Where(c => c.PublicId == requestMetadata!.PlatformVersion)
               .Select(c => new
               {
                   c.Id
               })
               .FirstOrDefaultAsync(cancellationToken);

        if (platformVersion is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = 1000,
                    Title = "Not valid request.",
                    Details = "The PlatformVersion not valid.",
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(request.Content!.EmailAddress))
        {
            var email = request.Content.EmailAddress
                .Trim()
                .Normalize(NormalizationForm.FormKC)
                .ToLowerInvariant();

            var clientEmail = await mizonDatabaseContext.ClientEmails
                .AsNoTracking()
                .Where(c => c.Email == email)
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    c.ClientId,
                    c.Client!.PublicId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (clientEmail is null)
            {
                return new BaseApiResponse<LoginResponse>
                {
                    Error = new BaseApiError
                    {
                        Code = 1001,
                        Title = "Login Failed",
                        Details = "Email or password is incorrect."
                    }
                };
            }

            clientPublicId = clientEmail.PublicId;
            clientId = clientEmail.ClientId;
        }
        else if (!string.IsNullOrWhiteSpace(request.Content.Username))
        {
            var username = request.Content.Username
                .Trim()
                .Normalize(NormalizationForm.FormKC)
                .ToLowerInvariant();

            var clientUsername = await mizonDatabaseContext.ClientUsernames
                .AsNoTracking()
                .Where(c => c.Username == username)
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    c.ClientId,
                    c.Client!.PublicId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (clientUsername is null)
            {
                return new BaseApiResponse<LoginResponse>
                {
                    Error = new BaseApiError
                    {
                        Code = 1001,
                        Title = "Login Failed",
                        Details = "Email or password is incorrect."
                    }
                };
            }

            clientPublicId = clientUsername.PublicId;
            clientId = clientUsername.ClientId;
        }
        else
        {
            return new BaseApiResponse<LoginResponse>
            {
                Error = new BaseApiError
                {
                    Code = 1001,
                    Title = "Login Failed",
                    Details = "Email or password is incorrect."
                }
            };
        }



        var clientPassword = await mizonDatabaseContext.ClientPasswords
            .AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .Where(c => c.IsActive)
            .Select(c => new
            {
                c.PasswordHash,
                c.Salt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (clientPassword is null)
        {
            return new BaseApiResponse<LoginResponse>
            {
                Error = new BaseApiError
                {
                    Code = 1001,
                    Title = "Login Failed",
                    Details = "Email or password is incorrect."
                }
            };
        }

        if (!VerifyPassword(request.Content.Password, clientPassword.PasswordHash, clientPassword.Salt))
        {
            return new BaseApiResponse<LoginResponse>
            {
                Error = new BaseApiError
                {
                    Code = 1001,
                    Title = "Login Failed",
                    Details = "Email or password is incorrect."
                }
            };
        }

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        var refreshTokenHashString = Convert.ToBase64String(refreshTokenHash);

        var clientSession = new ClientSession
        {
            PublicId = Guid.CreateVersion7(),
            ClientId = clientId!.Value,
            PlatformVersionId = platformVersion.Id,
            OsVersion = requestMetadata!.OsVersion!.ToString(),
            DeviceIdentifier = requestMetadata.DeviceIdentifier!,
            DeviceName = requestMetadata.DeviceName!,
            DeviceUsername = requestMetadata.DeviceUsername!,
            DeviceModel = requestMetadata.DeviceModel!,
            RefreshTokenHash = refreshTokenHashString,
            LastSeenDateTime = DateTimeOffset.UtcNow,
        };

        mizonDatabaseContext.ClientSessions.Add(clientSession);

        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                SessionPublicId = clientSession.PublicId,
                ClientPublicId = clientPublicId!.Value,
                ClientId = clientId!.Value,
                RefreshToken = refreshToken
            }
        };
    }


    private bool VerifyPassword(string password, byte[] storedHash, byte[] salt)
    {
        if (storedHash.Length != 32 || salt.Length < 16)
            return false;

        Span<byte> computedHash = stackalloc byte[32];

        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            computedHash,
            100000,
            HashAlgorithmName.SHA256);

        return CryptographicOperations.FixedTimeEquals(
            computedHash,
            storedHash);
    }
}
