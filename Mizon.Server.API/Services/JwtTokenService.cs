using Mizon.Server.API.JsonSerializerContexts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mizon.Server.API.Services;

public sealed class JwtTokenService
{
    private readonly byte[] _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenLifetime;

    public JwtTokenService(IConfiguration configuration)
    {
        // secret باید 256 بیت باشد (32 بایت)
        _secretKey = Convert.FromBase64String(configuration["Jwt:Secret"]!);

        _issuer = configuration["Jwt:Issuer"]!;
        _audience = configuration["Jwt:Audience"]!;

        // مثال: "00:15:00" → 15 دقیقه
        _accessTokenLifetime = TimeSpan.Parse(configuration["Jwt:AccessTokenLifetime"] ?? "00:15:00");
    }

    public string GenerateAccessToken(Guid clientPublicId, Guid sessionPublicId)
    {
        var now = DateTimeOffset.UtcNow;

        var payload = new JwtPayload
        {
            sub = clientPublicId.ToString(),
            sid = sessionPublicId.ToString(),
            iss = _issuer,
            aud = _audience,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(_accessTokenLifetime).ToUnixTimeSeconds()
        };

        return CreateJwt(payload);
    }

    private string CreateJwt(JwtPayload payload)
    {
        var header = new JwtHeader
        {
            alg = "HS256",
            typ = "JWT"
        };

        string headerJson = JsonSerializer.Serialize(header, JwtJsonContext.Default.JwtHeader);
        string payloadJson = JsonSerializer.Serialize(payload, JwtJsonContext.Default.JwtPayload);

        string headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        string payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        string unsignedToken = $"{headerBase64}.{payloadBase64}";

        string signature = CreateSignature(unsignedToken);

        return $"{unsignedToken}.{signature}";
    }

    public JwtValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new() { Error = JwtValidationError.InvalidFormat };

        var parts = token.Split('.');

        if (parts.Length != 3)
            return new() { Error = JwtValidationError.InvalidFormat };

        var header = parts[0];
        var payload = parts[1];
        var signature = parts[2];

        var unsignedToken = $"{header}.{payload}";

        // ----------------------------------------------------------
        // NEW (1): Decode + Validate HEADER (alg = HS256)
        // ----------------------------------------------------------
        JwtHeader? jwtHeader;

        try
        {
            var headerBytes = Base64UrlDecode(header);

            jwtHeader = JsonSerializer.Deserialize(
                headerBytes,
                JwtJsonContext.Default.JwtHeader);
        }
        catch
        {
            return new() { Error = JwtValidationError.InvalidFormat };
        }

        if (jwtHeader is null || jwtHeader.alg != "HS256")
            return new() { Error = JwtValidationError.InvalidFormat };
        // ----------------------------------------------------------

        if (!ValidateSignature(unsignedToken, signature))
            return new() { Error = JwtValidationError.InvalidSignature };

        JwtPayload? jwtPayload;

        try
        {
            var payloadBytes = Base64UrlDecode(payload);

            jwtPayload = JsonSerializer.Deserialize(
                payloadBytes,
                JwtJsonContext.Default.JwtPayload);
        }
        catch
        {
            return new() { Error = JwtValidationError.InvalidPayload };
        }

        if (jwtPayload == null)
            return new() { Error = JwtValidationError.InvalidPayload };

        if (jwtPayload.iss != _issuer)
            return new() { Error = JwtValidationError.InvalidIssuer };

        if (jwtPayload.aud != _audience)
            return new() { Error = JwtValidationError.InvalidAudience };

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ----------------------------------------------------------
        // NEW (2): Validate issued-at (iat should not be in future)
        // ----------------------------------------------------------
        if (jwtPayload.iat > now + 60) // allow up to 1 minute skew
            return new() { Error = JwtValidationError.InvalidPayload };
        // ----------------------------------------------------------

        if (jwtPayload.exp < now)
            return new() { Error = JwtValidationError.Expired };

        if (!Guid.TryParse(jwtPayload.sub, out var clientId))
            return new() { Error = JwtValidationError.InvalidPayload };

        if (!Guid.TryParse(jwtPayload.sid, out var sessionId))
            return new() { Error = JwtValidationError.InvalidPayload };

        return new JwtValidationResult
        {
            IsValid = true,
            ClientPublicId = clientId,
            SessionPublicId = sessionId
        };
    }

    private bool ValidateSignature(string data, string signature)
    {
        using var hmac = new HMACSHA256(_secretKey);

        Span<byte> hash = stackalloc byte[32];

        hmac.TryComputeHash(
            Encoding.UTF8.GetBytes(data),
            hash,
            out _);

        var computed = Base64UrlEncode(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signature));
    }

    private string CreateSignature(string data)
    {
        using var hmac = new HMACSHA256(_secretKey);

        Span<byte> hash = stackalloc byte[32];
        hmac.TryComputeHash(Encoding.UTF8.GetBytes(data), hash, out _);

        return Base64UrlEncode(hash);
    }


    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input
            .Replace("-", "+")
            .Replace("_", "/");

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    public sealed class JwtHeader
    {
        public string alg { get; set; } = "HS256";
        public string typ { get; set; } = "JWT";
    }

    public sealed class JwtPayload
    {
        public string sub { get; set; } = default!;
        public string sid { get; set; } = default!;
        public string iss { get; set; } = default!;
        public string aud { get; set; } = default!;
        public long iat { get; set; }
        public long exp { get; set; }
    }

    public enum JwtValidationError
    {
        None,
        InvalidFormat,
        InvalidSignature,
        Expired,
        InvalidIssuer,
        InvalidAudience,
        InvalidPayload
    }

    public sealed class JwtValidationResult
    {
        public bool IsValid { get; init; }
        public JwtValidationError Error { get; init; }
        public Guid ClientPublicId { get; init; }
        public Guid SessionPublicId { get; init; }
    }
}
