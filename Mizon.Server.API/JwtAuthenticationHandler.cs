using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Mizon.Server.API;

public sealed class JwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly JwtTokenService _jwt;
    private readonly IClientSessionAuthorization _sessions;

    public JwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        JwtTokenService jwt,
        IClientSessionAuthorization sessions)
        : base(options, logger, encoder)
    {
        _jwt = jwt;
        _sessions = sessions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. ابتدا از Authorization Header چک کن
        var token = ExtractTokenFromHeader();

        // 2. اگر در Header نبود، از QueryString چک کن (برای SignalR)
        if (string.IsNullOrEmpty(token))
        {
            token = ExtractTokenFromQueryString();
        }

        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var result = _jwt.ValidateToken(token);

        if (!result.IsValid)
            return AuthenticateResult.Fail("Invalid token");
        if (!await _sessions.ValidateAsync(result.ClientPublicId, result.SessionPublicId, true, Context.RequestAborted))
        {
            Response.Headers["Mizon-Session-Revoked"] = "1";
            return AuthenticateResult.Fail("Invalid or inactive session");
        }

        var clientPublicId = result.ClientPublicId.ToString();
        var sessionPublicId = result.SessionPublicId.ToString();
        var claims = new Claim[]
        {
            new Claim("sub", clientPublicId),
            new Claim("sid", sessionPublicId),
            new Claim(ClaimTypes.NameIdentifier, clientPublicId), // برای SignalR
            new Claim(ClaimTypes.UserData, sessionPublicId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }


    private string? ExtractTokenFromHeader()
    {
        var header = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(header))
            return null;

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        return header.AsSpan("Bearer ".Length).Trim().ToString();
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
