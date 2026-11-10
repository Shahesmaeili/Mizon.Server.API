using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Mizon.Server.Logic.Operations.Clients;

public sealed partial class RegisterOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<RegisterRequest, RegisterResponse>
{
    public async Task<BaseApiResponse<RegisterResponse>> HandleAsync(
        BaseApiRequest<RegisterRequest> request,
        RequestMetadata? requestMetadata,
        CancellationToken cancellationToken)
    {
        var content = request.Content;
        if (content is null)
            return InvalidInput("Registration details are required.");

        var firstName = content.FirstName?.Trim();
        var lastName = content.LastName?.Trim();
        if (!IsNameValid(firstName) || !IsNameValid(lastName))
            return InvalidInput("First name and last name must contain 1 to 60 characters.");

        // Bound identifiers before normalization as well as afterwards.
        if (string.IsNullOrWhiteSpace(content.Username) || content.Username.Length > 128)
            return InvalidInput("Username must contain 3 to 32 letters, numbers, dots or underscores.");
        if (string.IsNullOrWhiteSpace(content.EmailAddress) || content.EmailAddress.Length > 256)
            return InvalidInput("A valid email address of at most 128 characters is required.");
        if (content.Password is null || content.Password.Length is < 8 or > 128)
            return InvalidInput("Password must contain 8 to 128 characters.");

        string username;
        string email;
        try
        {
            username = content.Username.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            email = content.EmailAddress.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return InvalidInput("Username or email address contains invalid characters.");
        }

        if (username.Length is < 3 or > 32 || username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '.'))
            return InvalidInput("Username must contain 3 to 32 letters, numbers, dots or underscores.");
        if (email.Length > 128 || email.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c)) ||
            !EmailAddressPattern().IsMatch(email) ||
            !MailAddress.TryCreate(email, out var mailAddress) ||
            !string.Equals(mailAddress.Address, email, StringComparison.OrdinalIgnoreCase))
            return InvalidInput("A valid email address of at most 128 characters is required.");

        if (requestMetadata?.PlatformVersion is null || requestMetadata.OsVersion is null ||
            requestMetadata.OsVersion.ToString().Length > 16 ||
            !IsDeviceValueValid(requestMetadata.DeviceIdentifier) || !IsDeviceValueValid(requestMetadata.DeviceName) ||
            !IsDeviceValueValid(requestMetadata.DeviceUsername) || !IsDeviceValueValid(requestMetadata.DeviceModel))
            return Failure(1000, "Not valid request.", "Valid platform and device information is required.");

        var platformVersionId = await mizonDatabaseContext.PlatformVersions
            .AsNoTracking()
            .Where(c => c.PublicId == requestMetadata.PlatformVersion)
            .Select(c => (short?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (platformVersionId is null)
            return Failure(1000, "Not valid request.", "The PlatformVersion not valid.");

        // All five records commit together. Reuse an existing transaction when called by a larger operation.
        await using var transaction = mizonDatabaseContext.Database.CurrentTransaction is null
            ? await mizonDatabaseContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // The existing identifier tables have no unique indexes. Transaction-owned SQL Server locks
        // serialize registration of the same normalized identity across API instances, not just this process.
        try
        {
            await LockIdentifierAsync("username", username, cancellationToken);
            await LockIdentifierAsync("email", email, cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 51000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(OperationErrorCodes.Clients.RegistrationTemporarilyUnavailable,
                "Registration temporarily unavailable", "Please try again shortly.");
        }

        var usernameExists = await mizonDatabaseContext.ClientUsernames
            .AsNoTracking()
            .Where(c => c.Username == username)
            .Where(c => c.IsActive)
            .AnyAsync(cancellationToken);
        if (usernameExists)
            return Failure(OperationErrorCodes.Clients.UsernameUnavailable, "Registration failed", "This username is already in use.");

        var emailExists = await mizonDatabaseContext.ClientEmails
            .AsNoTracking()
            .Where(c => c.Email == email)
            .Where(c => c.IsActive)
            .AnyAsync(cancellationToken);
        if (emailExists)
            return Failure(OperationErrorCodes.Clients.EmailUnavailable, "Registration failed", "This email address is already in use.");

        cancellationToken.ThrowIfCancellationRequested();
        var salt = RandomNumberGenerator.GetBytes(16);
        // Do not trim or normalize passwords: this must match LoginOperation exactly.
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(content.Password, salt, 100000, HashAlgorithmName.SHA256, 32);
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var session = new ClientSession
        {
            PublicId = Guid.CreateVersion7(),
            PlatformVersionId = platformVersionId.Value,
            OsVersion = requestMetadata.OsVersion.ToString(),
            DeviceIdentifier = requestMetadata.DeviceIdentifier!,
            DeviceName = requestMetadata.DeviceName!,
            DeviceUsername = requestMetadata.DeviceUsername!,
            DeviceModel = requestMetadata.DeviceModel!,
            RefreshTokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))),
            LastSeenDateTime = DateTimeOffset.UtcNow
        };
        var client = new Client
        {
            PublicId = Guid.CreateVersion7(),
            FirstName = firstName!,
            LastName = lastName!,
            ClientUsernames = [new() { PublicId = Guid.CreateVersion7(), Username = username, IsActive = true }],
            ClientEmails = [new() { PublicId = Guid.CreateVersion7(), Email = email, IsActive = true, IsVerified = false }],
            ClientPasswords = [new() { Salt = salt, PasswordHash = passwordHash, IsActive = true }],
            ClientSessions = [session]
        };

        mizonDatabaseContext.Clients.Add(client);
        await mizonDatabaseContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return new()
        {
            Content = new()
            {
                ClientId = client.Id,
                ClientPublicId = client.PublicId,
                SessionPublicId = session.PublicId,
                RefreshToken = refreshToken
            }
        };
    }

    private async Task LockIdentifierAsync(string kind, string identifier, CancellationToken cancellationToken)
    {
        var resource = $"Mizon:Register:{kind}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))}";
        await mizonDatabaseContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock @Resource = {resource},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @lockResult < 0
                THROW 51000, 'Unable to acquire the registration identity lock.', 1;
            """, cancellationToken);
    }

    private static bool IsNameValid(string? value) =>
        value is { Length: > 0 and <= 60 } && !value.Any(char.IsControl);

    private static bool IsDeviceValueValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128;

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressPattern();

    private static BaseApiResponse<RegisterResponse> InvalidInput(string details) =>
        Failure(OperationErrorCodes.Clients.InvalidRegistration, "Invalid registration details", details);

    private static BaseApiResponse<RegisterResponse> Failure(int code, string title, string details) => new()
    {
        Error = new() { Code = code, Title = title, Details = details }
    };
}
