# Development HTTPS on a changing LAN

- Owner: Codex
- Date: 2026-11-21
- Owned areas: API startup, local Kestrel listener configuration and HTTPS verification.

## Behavior

The standard launch keeps the programmatic localhost endpoint. To use a
shared LAN and localhost port in Development, set
`Server:UseConfiguredDevelopmentEndpoints` to true and declare the HTTPS
listener in `Kestrel:Endpoints` in the ignored
`Mizon.Server.API/appsettings.Development.json`. Use an all-interface URL
such as `https://[::]:5128` with `Http1AndHttp2AndHttp3` and a development
certificate covering the DNS name clients use. Binding the wildcard address
keeps startup independent of the current DHCP address.

The opted-in path calls `UseKestrelHttpsConfiguration` because the app uses
`CreateSlimBuilder`. To preserve the currently trusted localhost certificate
on the same listener, set `Server:DevelopmentLocalhostCertificateThumbprint`
to its thumbprint from the current user's personal certificate store. Keep
machine-specific configuration and certificates outside version control.
The selector chooses the localhost certificate by name and keeps the configured
certificate for LAN traffic. Kestrel negotiates the application protocol for
each transport.

## Validation

- .NET 11 Debug build completed.
- Unauthenticated `GET /Clients/Me` returned HTTP 401 over HTTP/2 and HTTP/3
  from Windows for localhost, the LAN address and the LAN DNS name. The tests
  validated the applicable certificate and endpoint name.
- The macOS Swift SDK reached the LAN DNS name over HTTP/3 and received HTTP 401.
- Startup asset provisioning was disabled in the local development settings
  during verification. No database migration was run.
