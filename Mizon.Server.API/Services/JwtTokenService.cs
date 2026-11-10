using Mizon.Server.API.JsonSerializerContexts;
using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Mizon.Server.API.Services;

public sealed class JwtTokenService
{
    private static readonly string EncodedHeader = Base64Url.EncodeToString(
        JsonSerializer.SerializeToUtf8Bytes(new JwtHeader(), JwtJsonContext.Default.JwtHeader));
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
        var payloadBase64 = Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(payload, JwtJsonContext.Default.JwtPayload));
        string unsignedToken = $"{EncodedHeader}.{payloadBase64}";

        string signature = CreateSignature(unsignedToken);

        return $"{unsignedToken}.{signature}";
    }

    public JwtValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new() { Error = JwtValidationError.InvalidFormat };

        var value = token.AsSpan();
        var headerEnd = value.IndexOf('.');
        if (headerEnd < 0)
            return new() { Error = JwtValidationError.InvalidFormat };
        var remaining = value[(headerEnd + 1)..];
        var payloadEnd = remaining.IndexOf('.');
        if (payloadEnd < 0 || remaining[(payloadEnd + 1)..].Contains('.'))
            return new() { Error = JwtValidationError.InvalidFormat };

        var header = value[..headerEnd];
        var payload = remaining[..payloadEnd];
        var signature = remaining[(payloadEnd + 1)..];
        var unsignedToken = value[..(headerEnd + 1 + payloadEnd)];

        if (!TryReadTokenPart(header, JwtJsonContext.Default.JwtHeader, out var jwtHeader)
            || jwtHeader is null || jwtHeader.alg != "HS256")
            return new() { Error = JwtValidationError.InvalidFormat };

        if (!ValidateSignature(unsignedToken, signature))
            return new() { Error = JwtValidationError.InvalidSignature };

        if (!TryReadTokenPart(payload, JwtJsonContext.Default.JwtPayload, out var jwtPayload)
            || jwtPayload is null)
            return new() { Error = JwtValidationError.InvalidPayload };

        if (jwtPayload.iss != _issuer)
            return new() { Error = JwtValidationError.InvalidIssuer };

        if (jwtPayload.aud != _audience)
            return new() { Error = JwtValidationError.InvalidAudience };

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (jwtPayload.iat > now + 60) // allow up to 1 minute skew
            return new() { Error = JwtValidationError.InvalidPayload };

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

    private bool ValidateSignature(ReadOnlySpan<char> data, ReadOnlySpan<char> signature)
    {
        // HS256 produces exactly 43 unpadded Base64Url characters. Compare the
        // canonical representation as before, without accepting padded aliases.
        if (signature.Length != 43)
            return false;
        Span<byte> received = stackalloc byte[43];
        if (!Encoding.UTF8.TryGetBytes(signature, received, out var receivedLength) || receivedLength != received.Length)
            return false;
        Span<byte> hash = stackalloc byte[32];
        ComputeSignature(data, hash);
        Span<byte> expected = stackalloc byte[43];
        Base64Url.EncodeToUtf8(hash, expected);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private string CreateSignature(ReadOnlySpan<char> data)
    {
        Span<byte> hash = stackalloc byte[32];
        ComputeSignature(data, hash);
        return Base64Url.EncodeToString(hash);
    }


    private void ComputeSignature(ReadOnlySpan<char> data, Span<byte> hash)
    {
        var byteCount = Encoding.UTF8.GetByteCount(data);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= 1024
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        try
        {
            var written = Encoding.UTF8.GetBytes(data, bytes);
            HMACSHA256.HashData(_secretKey, bytes[..written], hash);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static bool TryReadTokenPart<T>(ReadOnlySpan<char> input, JsonTypeInfo<T> typeInfo, out T? value)
        where T : class
    {
        value = null;
        // Preserve the previous decoder's treatment of padded Base64/header
        // whitespace; newly issued tokens still use canonical unpadded Base64Url.
        var padding = (input.Length % 4) switch { 2 => 2, 3 => 1, _ => 0 };
        var length = input.Length + padding;
        var maximumBytes = length / 4 * 3;
        char[]? rentedChars = null;
        byte[]? rentedBytes = null;
        Span<char> normalized = length <= 1024
            ? stackalloc char[length]
            : (rentedChars = ArrayPool<char>.Shared.Rent(length)).AsSpan(0, length);
        Span<byte> decoded = maximumBytes <= 768
            ? stackalloc byte[maximumBytes]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(maximumBytes));
        try
        {
            for (var index = 0; index < input.Length; index++)
                normalized[index] = input[index] switch { '-' => '+', '_' => '/', var character => character };
            normalized[input.Length..].Fill('=');
            if (!Convert.TryFromBase64Chars(normalized, decoded, out var written))
                return false;
            value = JsonSerializer.Deserialize(decoded[..written], typeInfo);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            if (rentedChars is not null) ArrayPool<char>.Shared.Return(rentedChars, clearArray: true);
            if (rentedBytes is not null) ArrayPool<byte>.Shared.Return(rentedBytes, clearArray: true);
        }
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
