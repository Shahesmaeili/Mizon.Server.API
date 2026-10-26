using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Mizon.Server.API;

public sealed class JwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly JwtTokenService _jwt;

    public JwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        JwtTokenService jwt)
        : base(options, logger, encoder)
    {
        _jwt = jwt;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. ابتدا از Authorization Header چک کن
        var token = ExtractTokenFromHeader();

        // 2. اگر در Header نبود، از QueryString چک کن (برای SignalR)
        if (string.IsNullOrEmpty(token))
        {
            token = ExtractTokenFromQueryString();
        }

        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var result = _jwt.ValidateToken(token);

        if (!result.IsValid)
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));

        var claims = new Claim[]
        {
            new Claim("sub", result.ClientPublicId.ToString()),
            new Claim("sid", result.SessionPublicId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, result.ClientPublicId.ToString()), // برای SignalR
            new Claim(ClaimTypes.UserData, result.SessionPublicId.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }


    private string? ExtractTokenFromHeader()
    {
        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header))
            return null;

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        return header.Substring("Bearer ".Length).Trim();
    }

    private string? ExtractTokenFromQueryString()
    {
        // SignalR از query string با نام access_token استفاده می‌کند
        var queryToken = Request.Query["access_token"].ToString();

        if (!string.IsNullOrEmpty(queryToken))
            return queryToken;

        // یا اگر از پارامتر custom استفاده می‌کنید
        return Request.Query["token"].ToString();
    }
}
